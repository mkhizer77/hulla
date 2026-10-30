// The iOS share sheet for NativeShareSheet (GDD 10.5, 10.9): text, or a PNG with a caption.
#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

static void HullaPresentShare(NSArray* items)
{
    UIViewController* root = UnityGetGLViewController();
    if (root == nil)
    {
        return;
    }

    UIActivityViewController* sheet = [[UIActivityViewController alloc] initWithActivityItems:items applicationActivities:nil];
    UIPopoverPresentationController* popover = sheet.popoverPresentationController;
    if (popover != nil)
    {
        // iPad shows the sheet as a popover: anchor it at the bottom centre with no arrow.
        popover.sourceView = root.view;
        popover.sourceRect = CGRectMake(CGRectGetMidX(root.view.bounds), CGRectGetMaxY(root.view.bounds) - 1, 1, 1);
        popover.permittedArrowDirections = 0;
    }

    [root presentViewController:sheet animated:YES completion:nil];
}

extern "C" bool HullaShareText(const char* text)
{
    if (text == NULL)
    {
        return false;
    }

    NSString* body = [NSString stringWithUTF8String:text];
    dispatch_async(dispatch_get_main_queue(), ^{ HullaPresentShare(@[ body ]); });
    return true;
}

extern "C" bool HullaShareImage(const char* path, const char* text)
{
    if (path == NULL)
    {
        return false;
    }

    UIImage* image = [UIImage imageWithContentsOfFile:[NSString stringWithUTF8String:path]];
    if (image == nil)
    {
        return false;
    }

    NSMutableArray* items = [NSMutableArray arrayWithObject:image];
    if (text != NULL)
    {
        [items addObject:[NSString stringWithUTF8String:text]];
    }

    dispatch_async(dispatch_get_main_queue(), ^{ HullaPresentShare(items); });
    return true;
}
