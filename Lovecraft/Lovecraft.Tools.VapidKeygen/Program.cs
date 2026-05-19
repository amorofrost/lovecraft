using WebPush;

// Generate a fresh VAPID keypair
var vapidKeys = VapidHelper.GenerateVapidKeys();

// Print to stdout in environment variable format
Console.WriteLine($"VAPID_PUBLIC_KEY={vapidKeys.PublicKey}");
Console.WriteLine($"VAPID_PRIVATE_KEY={vapidKeys.PrivateKey}");
Console.WriteLine($"VAPID_SUBJECT=mailto:noreply@aloeband.ru");
