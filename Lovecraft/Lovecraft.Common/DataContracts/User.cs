using System;

namespace Lovecraft.Common.DataContracts
{
    public class User
    {
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
