using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DataContracts;
using Lovecraft.WebAPI.Repositories;

namespace Lovecraft.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = req.Name,
                AvatarUri = req.AvatarUri,
                TelegramUserId = req.TelegramUserId,
                TelegramUsername = req.TelegramUsername,
                TelegramAvatarFileId = req.TelegramAvatarFileId,
                CreatedAt = DateTime.UtcNow,
                Version = Guid.NewGuid().ToString()
            };

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
    }
}
