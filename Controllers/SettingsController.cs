using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Net.Http;

namespace backend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SettingsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

        [HttpGet("telegram")]
        public async Task<IActionResult> GetTelegramBot()
        {
            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            return Ok(new { token = user.TelegramBotToken });
        }

        [HttpPost("telegram")]
        public async Task<IActionResult> UpdateTelegramBot([FromBody] TelegramUpdateDto dto)
        {
            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            var token = dto.Token?.Trim() ?? "";

            // Validate token if provided
            if (!string.IsNullOrEmpty(token))
            {
                using var client = new HttpClient();
                var url = $"https://api.telegram.org/bot{token}/getMe";
                try
                {
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        return BadRequest(new { message = "Invalid Telegram Bot Token. Please check and try again." });
                    }
                }
                catch
                {
                    return BadRequest(new { message = "Failed to connect to Telegram API to verify the token." });
                }
            }

            user.TelegramBotToken = token;
            user.LastTelegramUpdateId = 0; // reset
            user.TelegramChatId = ""; // reset
            await _context.SaveChangesAsync();

            return Ok(new { message = "Telegram bot updated successfully!" });
        }
    }

    public class TelegramUpdateDto
    {
        public string Token { get; set; } = string.Empty;
    }
}
