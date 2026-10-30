// Sign-in on iOS for NativeSocialSignIn (GDD 10.2, #38): Sign in with Apple, and a browser sign-in
// session (ASWebAuthenticationSession) for Google's OAuth with PKCE. No third-party SDK. Also the
// address book's phone numbers for NativeContacts (people you may know).
#import <AuthenticationServices/AuthenticationServices.h>
#import <Contacts/Contacts.h>
#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

typedef void (*HullaSocialCallback)(const char* value, const char* error);

static void HullaSocialReply(HullaSocialCallback callback, NSString* value, NSString* error)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        callback(value != nil ? [value UTF8String] : NULL, error != nil ? [error UTF8String] : NULL);
    });
}

static UIWindow* HullaSocialWindow()
{
    UIViewController* root = UnityGetGLViewController();
    return root.view.window;
}

@interface HullaAppleSignIn : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property (nonatomic, assign) HullaSocialCallback callback;
@end

@implementation HullaAppleSignIn

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithAuthorization:(ASAuthorization*)authorization
{
    NSString* token = nil;
    if ([authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]])
    {
        NSData* data = ((ASAuthorizationAppleIDCredential*)authorization.credential).identityToken;
        if (data != nil)
        {
            token = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        }
    }

    HullaSocialReply(self.callback, token, token == nil ? @"auth.provider_unavailable" : nil);
}

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithError:(NSError*)error
{
    BOOL cancelled = error.code == ASAuthorizationErrorCanceled;
    HullaSocialReply(self.callback, nil, cancelled ? @"auth.cancelled" : @"auth.provider_unavailable");
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController*)controller
{
    return HullaSocialWindow();
}

@end

@interface HullaWebAuthAnchor : NSObject <ASWebAuthenticationPresentationContextProviding>
@end

@implementation HullaWebAuthAnchor

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession*)session
{
    return HullaSocialWindow();
}

@end

static HullaAppleSignIn* HullaAppleDelegate;
static HullaWebAuthAnchor* HullaWebAnchor;
static ASWebAuthenticationSession* HullaWebSession;

extern "C" void HullaAppleSignInStart(HullaSocialCallback callback)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        ASAuthorizationAppleIDRequest* request = [[[ASAuthorizationAppleIDProvider alloc] init] createRequest];
        request.requestedScopes = @[ ASAuthorizationScopeFullName ];
        ASAuthorizationController* controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[ request ]];
        HullaAppleDelegate = [[HullaAppleSignIn alloc] init];
        HullaAppleDelegate.callback = callback;
        controller.delegate = HullaAppleDelegate;
        controller.presentationContextProvider = HullaAppleDelegate;
        [controller performRequests];
    });
}

/// Opens `url` in a private browser sign-in sheet and replies with the redirect back to `scheme`.
extern "C" void HullaWebAuthStart(const char* url, const char* scheme, HullaSocialCallback callback)
{
    NSURL* start = [NSURL URLWithString:[NSString stringWithUTF8String:url]];
    NSString* callbackScheme = [NSString stringWithUTF8String:scheme];
    dispatch_async(dispatch_get_main_queue(), ^{
        HullaWebSession = [[ASWebAuthenticationSession alloc] initWithURL:start callbackURLScheme:callbackScheme completionHandler:^(NSURL* callbackURL, NSError* error) {
            HullaWebSession = nil;
            if (error != nil)
            {
                BOOL cancelled = error.code == ASWebAuthenticationSessionErrorCodeCanceledLogin;
                HullaSocialReply(callback, nil, cancelled ? @"auth.cancelled" : @"auth.provider_unavailable");
                return;
            }

            HullaSocialReply(callback, callbackURL.absoluteString, nil);
        }];
        HullaWebAnchor = [[HullaWebAuthAnchor alloc] init];
        HullaWebSession.presentationContextProvider = HullaWebAnchor;
        HullaWebSession.prefersEphemeralWebBrowserSession = NO;
        if (![HullaWebSession start])
        {
            HullaWebSession = nil;
            HullaSocialReply(callback, nil, @"auth.provider_unavailable");
        }
    });
}

/// Asks for the address book once, then replies with every phone number as a JSON array of strings.
extern "C" void HullaContactsRead(HullaSocialCallback callback)
{
    CNContactStore* store = [[CNContactStore alloc] init];
    [store requestAccessForEntityType:CNEntityTypeContacts completionHandler:^(BOOL granted, NSError* error) {
        if (!granted)
        {
            HullaSocialReply(callback, nil, @"contacts.denied");
            return;
        }

        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            NSMutableArray<NSString*>* numbers = [NSMutableArray array];
            CNContactFetchRequest* request = [[CNContactFetchRequest alloc] initWithKeysToFetch:@[ CNContactPhoneNumbersKey ]];
            NSError* fetchError = nil;
            [store enumerateContactsWithFetchRequest:request error:&fetchError usingBlock:^(CNContact* contact, BOOL* stop) {
                for (CNLabeledValue<CNPhoneNumber*>* value in contact.phoneNumbers)
                {
                    [numbers addObject:value.value.stringValue];
                }
            }];
            NSData* json = [NSJSONSerialization dataWithJSONObject:numbers options:0 error:nil];
            NSString* text = json != nil ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : nil;
            HullaSocialReply(callback, text, text == nil ? @"contacts.unavailable" : nil);
        });
    }];
}
