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
    public class TransactionsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TransactionsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

        [HttpGet]
        public async Task<IActionResult> GetTransactions([FromQuery] int? accountId, [FromQuery] string? type, [FromQuery] string? webManagementId)
        {
            var userId = GetUserId();
            var query = _context.Transactions.Where(t => t.UserId == userId);

            if (accountId.HasValue)
                query = query.Where(t => t.AccountId == accountId.Value);

            if (!string.IsNullOrEmpty(type))
                query = query.Where(t => t.Type == type);

            if (!string.IsNullOrEmpty(webManagementId))
                query = query.Where(t => t.WebManagementId != null && t.WebManagementId.Contains(webManagementId));

            var transactions = await query.OrderByDescending(t => t.Date).ToListAsync();
            return Ok(transactions);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTransaction(Transaction transaction)
        {
            transaction.UserId = GetUserId();
            transaction.Date = DateTime.UtcNow;
            
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == transaction.AccountId && a.UserId == transaction.UserId);
            if (account == null) return BadRequest("Account not found");

            if (transaction.Type == "Withdraw" && string.IsNullOrWhiteSpace(transaction.WebManagementId))
            {
                return BadRequest("Web Management ID is required for withdrawals.");
            }

            if (transaction.Type == "Deposit")
            {
                account.CurrentBalance += transaction.Amount;
            }
            else if (transaction.Type == "Withdraw")
            {
                account.CurrentBalance -= transaction.Amount;
            }
            else if (transaction.Type == "Transfer")
            {
                var toAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == transaction.ToAccountId && a.UserId == transaction.UserId);
                if (toAccount == null) return BadRequest("Target account not found");
                
                account.CurrentBalance -= transaction.Amount;
                toAccount.CurrentBalance += transaction.Amount;
            }

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();
            return Ok(transaction);
        }
    }
}
