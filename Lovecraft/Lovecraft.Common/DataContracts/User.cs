using System;

namespace Lovecraft.Common.DataContracts
{
    public class User
    {
        // Maximum allowed lengths for string fields
        public const int MaxNameLength = 255;
        public const int MaxTelegramUsernameLength = 255;
        public const int MaxAvatarUriLength = 255;

        public static void ValidateLengthsOrThrow(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (user.Name != null && user.Name.Length > MaxNameLength)
                throw new ArgumentException($"Name must be at most {MaxNameLength} characters long", nameof(user.Name));
            if (user.TelegramUsername != null && user.TelegramUsername.Length > MaxTelegramUsernameLength)
                throw new ArgumentException($"TelegramUsername must be at most {MaxTelegramUsernameLength} characters long", nameof(user.TelegramUsername));
            if (user.AvatarUri != null && user.AvatarUri.Length > MaxAvatarUriLength)
                throw new ArgumentException($"AvatarUri must be at most {MaxAvatarUriLength} characters long", nameof(user.AvatarUri));
        }
        // Required
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string AvatarUri { get; set; } = string.Empty; // string or URI
        public string Version { get; set; } = string.Empty; // ETag-like version

        // Optional
        public long? TelegramUserId { get; set; }
        public string? TelegramUsername { get; set; }
        public string? TelegramAvatarFileId { get; set; }
    }
}
