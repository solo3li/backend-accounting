using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace backend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AccountsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

        [HttpGet]
        public async Task<IActionResult> GetAccounts()
        {
            var userId = GetUserId();
            var accounts = await _context.Accounts.Where(a => a.UserId == userId).ToListAsync();
            return Ok(accounts);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAccount(Account account)
        {
            account.UserId = GetUserId();
            account.CurrentBalance = account.InitialBalance;
            account.CreatedAt = DateTime.UtcNow;
            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();
            return Ok(account);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAccount(int id, Account updatedAccount)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == GetUserId());
            if (account == null) return NotFound();

            account.Name = updatedAccount.Name;
            account.Type = updatedAccount.Type;
            await _context.SaveChangesAsync();
            return Ok(account);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAccount(int id)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == GetUserId());
            if (account == null) return NotFound();

            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
