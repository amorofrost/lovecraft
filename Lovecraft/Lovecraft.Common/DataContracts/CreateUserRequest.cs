using System;

namespace Lovecraft.Common.DataContracts
{
    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string AvatarUri { get; set; } = string.Empty;
        public long? TelegramUserId { get; set; }
        public string? TelegramUsername { get; set; }
        public string? TelegramAvatarFileId { get; set; }
    }
}
