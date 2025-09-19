using System;

namespace Lovecraft.WebAPI.Repositories
{
    public class DuplicateUsernameException : Exception
    {
        public DuplicateUsernameException(string username)
            : base($"Username '{username}' is already in use.")
        {
        }
    }
}
