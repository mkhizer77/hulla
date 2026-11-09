// The table's haptics on iOS (GDD 16.4): UIKit's impact and notification feedback, the same
// events Android plays as vibration patterns. Called from Haptics.Play through HullaHaptic.
#import <UIKit/UIKit.h>

extern "C" void HullaHaptic(int kind)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        switch (kind)
        {
            case 0: // turn start: a light tick
            {
                UIImpactFeedbackGenerator* tick = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                [tick impactOccurred];
                break;
            }
            case 1: // card played: soft
            {
                UIImpactFeedbackGenerator* soft = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
                [soft impactOccurred];
                break;
            }
            case 2: // thulla given: medium
            {
                UIImpactFeedbackGenerator* medium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [medium impactOccurred];
                break;
            }
            case 3: // picked up: heavy
            {
                UIImpactFeedbackGenerator* heavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
                [heavy impactOccurred];
                break;
            }
            case 4: // escaped: success
            {
                UINotificationFeedbackGenerator* note = [[UINotificationFeedbackGenerator alloc] init];
                [note notificationOccurred:UINotificationFeedbackTypeSuccess];
                break;
            }
            default: // the Bhabhi: error
            {
                UINotificationFeedbackGenerator* note = [[UINotificationFeedbackGenerator alloc] init];
                [note notificationOccurred:UINotificationFeedbackTypeError];
                break;
            }
        }
    });
}
