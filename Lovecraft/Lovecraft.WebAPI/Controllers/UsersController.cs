using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DataContracts;
using Lovecraft.WebAPI.Repositories;

namespace Lovecraft.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly IUserRepository _repo;

        public UsersController(IUserRepository repo)
        {
            _repo = repo;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
        {
            if (req == null)
                return BadRequest("Request body is required");
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Name is required");
            if (string.IsNullOrWhiteSpace(req.AvatarUri))
                return BadRequest("AvatarUri is required");

            if (req.Name.Length > Common.DataContracts.User.MaxNameLength)
                return BadRequest($"Name must be at most {Common.DataContracts.User.MaxNameLength} characters long");
            if (!string.IsNullOrWhiteSpace(req.TelegramUsername) && req.TelegramUsername!.Length > Common.DataContracts.User.MaxTelegramUsernameLength)
                return BadRequest($"TelegramUsername must be at most {Common.DataContracts.User.MaxTelegramUsernameLength} characters long");
            if (req.AvatarUri.Length > Common.DataContracts.User.MaxAvatarUriLength)
                return BadRequest($"AvatarUri must be at most {Common.DataContracts.User.MaxAvatarUriLength} characters long");
            // Validate optional username/password lengths
            if (!string.IsNullOrWhiteSpace(req.Username) && req.Username!.Length > Common.DataContracts.User.MaxUsernameLength)
                return BadRequest($"Username must be at most {Common.DataContracts.User.MaxUsernameLength} characters long");
            if (!string.IsNullOrWhiteSpace(req.Password) && req.Password!.Length > Common.DataContracts.User.MaxPasswordHashLength)
                return BadRequest($"Password must be at most {Common.DataContracts.User.MaxPasswordHashLength} characters long");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = req.Name,
                AvatarUri = req.AvatarUri,
                TelegramUserId = req.TelegramUserId,
                TelegramUsername = req.TelegramUsername,
                TelegramAvatarFileId = req.TelegramAvatarFileId,
                // Credentials (if provided) will be set below after hashing the password
                CreatedAt = DateTime.UtcNow,
                Version = Guid.NewGuid().ToString()
            };

            // If username/password provided, hash the password and attach credentials to the user
            if (!string.IsNullOrWhiteSpace(req.Username) || !string.IsNullOrWhiteSpace(req.Password))
            {
                // Require both username and password together
                if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                    return BadRequest("Both Username and Password must be provided together for credential-based accounts");

                // Normalize username (trim)
                var username = req.Username!.Trim();
                // Normalize for storage/lookup
                var normalizedUsername = username.ToLowerInvariant();

                if (normalizedUsername.Length > Common.DataContracts.User.MaxUsernameLength)
                    return BadRequest($"Username must be at most {Common.DataContracts.User.MaxUsernameLength} characters long");

                // Pre-check uniqueness
                var existingByUsername = await _repo.GetByUsernameAsync(normalizedUsername);
                if (existingByUsername != null)
                    return Conflict(new { message = "Username is already taken" });

                // Hash the password (PBKDF2)
                var passwordHash = HashPassword(req.Password!);

                user.Username = normalizedUsername;
                user.PasswordHash = passwordHash;
            }

            try
            {
                var created = await _repo.CreateAsync(user);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (DuplicateTelegramUserIdException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (DuplicateTelegramUsernameException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Lovecraft.WebAPI.Repositories.DuplicateUsernameException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // Simple PBKDF2 hashing helper. Produces a base64 string with format: {iterations}.{saltBase64}.{hashBase64}
        private static string HashPassword(string password, int iterations = 120_000)
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var salt = new byte[16];
            rng.GetBytes(salt);

            using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);

            return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();
            return Ok(user);
        }

        [HttpGet("byTelegramId/{telegramId:long}")]
        public async Task<IActionResult> GetByTelegramId(long telegramId)
        {
            var user = await _repo.GetByTelegramUserIdAsync(telegramId);
            if (user == null) return NotFound();
            return Ok(user);
        }

        [HttpGet("byTelegramUsername/{username}")]
        public async Task<IActionResult> GetByTelegramUsername(string username)
        {
            var user = await _repo.GetByTelegramUsernameAsync(username);
            if (user == null) return NotFound();
            return Ok(user);
        }

        [HttpGet("next")]
        public async Task<IActionResult> GetNextProfile()
        {
            var user = await _repo.GetRandomAsync();
            if (user == null) return NotFound();
            return Ok(user);
        }

        [HttpGet("usernameAvailable/{username}")]
        public async Task<IActionResult> UsernameAvailable(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest(new { available = false, reason = "username required" });

            var normalized = username.Trim().ToLowerInvariant();
            if (normalized.Length > Common.DataContracts.User.MaxUsernameLength)
                return BadRequest(new { available = false, reason = "too long" });

            var existing = await _repo.GetByUsernameAsync(normalized);
            if (existing != null)
                return Conflict(new { available = false, reason = "username taken" });

            return Ok(new { available = true });
        }
    }
}
