using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;

namespace backend.Services
{
    public class TelegramBotService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly HttpClient _httpClient;
        private readonly string _openRouterApiKey;

        public TelegramBotService(IServiceProvider serviceProvider, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _httpClient = new HttpClient();
            _openRouterApiKey = configuration["OpenRouterApiKey"] ?? "";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var users = await context.Users.Where(u => u.TelegramBotToken != "").ToListAsync();

                        foreach (var user in users)
                        {
                            await ProcessUserBot(user, context);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in TelegramBotService: {ex.Message}");
                }

                await Task.Delay(3000, stoppingToken);
            }
        }

        private async Task ProcessUserBot(User user, AppDbContext context)
        {
            try
            {
                var offset = user.LastTelegramUpdateId + 1;
                var url = $"https://api.telegram.org/bot{user.TelegramBotToken}/getUpdates?offset={offset}&timeout=2";
                
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return;

                var content = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(content);
                var ok = doc.RootElement.GetProperty("ok").GetBoolean();
                if (!ok) return;

                var result = doc.RootElement.GetProperty("result");
                foreach (var update in result.EnumerateArray())
                {
                    long updateId = update.GetProperty("update_id").GetInt64();
                    user.LastTelegramUpdateId = updateId;
                    
                    if (update.TryGetProperty("message", out var message) && message.TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();
                        var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
                        user.TelegramChatId = chatId.ToString();

                        await HandleMessageWithAI(user, chatId, text, context);
                    }
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Silent catch for network errors
            }
        }

        private async Task HandleMessageWithAI(User user, long chatId, string text, AppDbContext context)
        {
            try
            {
                var userAccounts = await context.Accounts.Where(a => a.UserId == user.Id).ToListAsync();
                var accountsContext = userAccounts.Count > 0 
                    ? string.Join("\n", userAccounts.Select(a => $"- {a.Name} (Balance: {a.CurrentBalance}$)"))
                    : "No accounts found.";

                var systemPrompt = $@"You are a helpful finance assistant bot inside a Cashflow Tracker app. 
You can understand the user's intent and return a JSON object with your action. 
User's Current Accounts:
{accountsContext}

Valid actions:
1. {{ ""action"": ""reply"", ""message"": ""your response text in Arabic"" }} - use this for general chat, questions, or if the requested account is not found. Always reply in Arabic.
2. {{ ""action"": ""add_transaction"", ""type"": ""Deposit|Withdraw"", ""amount"": 123.45, ""account_name"": ""name of the account"", ""notes"": ""optional details"" }}
3. {{ ""action"": ""add_transaction"", ""type"": ""Transfer"", ""amount"": 123.45, ""from_account"": ""name of source account"", ""to_account"": ""name of destination account"", ""notes"": ""optional details"" }}
4. {{ ""action"": ""search_transactions"", ""query"": ""Search keyword (e.g. 'rent', 'bank', '12345'). Leave EMPTY if user just wants recent transactions without specific search."" }}
5. {{ ""action"": ""get_transaction_details"", ""query"": ""Exact transaction ID or search keyword."" }}
6. {{ ""action"": ""get_info"" }} - use this to return account balances summary.
Always output ONLY valid JSON without any markdown or backticks. Understand Arabic input smoothly and generate friendly Arabic text in the message field.";

                var payload = new
                {
                    model = "qwen/qwen3.6-plus",
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var aiResponse = await _httpClient.SendAsync(request);
                var aiContent = await aiResponse.Content.ReadAsStringAsync();
                
                var aiDoc = JsonDocument.Parse(aiContent);
                var replyText = aiDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

                var jsonMatch = System.Text.RegularExpressions.Regex.Match(replyText, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (jsonMatch.Success) replyText = jsonMatch.Value;

                var actionDoc = JsonDocument.Parse(replyText);
                var action = actionDoc.RootElement.GetProperty("action").GetString();
                
                string botReply = "";

                if (action == "reply")
                {
                    botReply = actionDoc.RootElement.GetProperty("message").GetString();
                }
                else if (action == "add_transaction")
                {
                    var type = actionDoc.RootElement.GetProperty("type").GetString();
                    var amount = actionDoc.RootElement.GetProperty("amount").GetDecimal();
                    var notes = actionDoc.RootElement.TryGetProperty("notes", out var n) ? n.GetString() : "";

                    if (type == "Transfer")
                    {
                        var fromAccountName = actionDoc.RootElement.TryGetProperty("from_account", out var fa) ? fa.GetString() : "";
                        var toAccountName = actionDoc.RootElement.TryGetProperty("to_account", out var ta) ? ta.GetString() : "";
                        
                        var fromAccount = userAccounts.FirstOrDefault(a => a.Name.Equals(fromAccountName, StringComparison.OrdinalIgnoreCase));
                        var toAccount = userAccounts.FirstOrDefault(a => a.Name.Equals(toAccountName, StringComparison.OrdinalIgnoreCase));

                        if (fromAccount == null || toAccount == null)
                        {
                            botReply = "عذراً، لم أتمكن من العثور على الحسابات المطلوبة للتحويل.";
                        }
                        else if (fromAccount.CurrentBalance < amount)
                        {
                            botReply = $"عذراً، الرصيد في حساب {fromAccount.Name} غير كافٍ لإتمام التحويل.";
                        }
                        else
                        {
                            var t = new Transaction { UserId = user.Id, AccountId = fromAccount.Id, ToAccountId = toAccount.Id, Type = type, Amount = amount, Notes = notes, Date = DateTime.UtcNow, WebManagementId = "Bot" };
                            fromAccount.CurrentBalance -= amount;
                            toAccount.CurrentBalance += amount;
                            context.Transactions.Add(t);
                            await context.SaveChangesAsync();
                            botReply = $"تم تحويل {amount}$ من حساب {fromAccount.Name} إلى حساب {toAccount.Name} بنجاح.";
                        }
                    }
                    else
                    {
                        var accountName = actionDoc.RootElement.TryGetProperty("account_name", out var an) ? an.GetString() : "";
                        var account = string.IsNullOrEmpty(accountName) ? userAccounts.FirstOrDefault() : userAccounts.FirstOrDefault(a => a.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));

                        if (account == null)
                        {
                            botReply = "عذراً، لم أتمكن من العثور على الحساب المطلوب.";
                        }
                        else if (type == "Withdraw" && account.CurrentBalance < amount)
                        {
                            botReply = $"عذراً، الرصيد في حساب {account.Name} غير كافٍ لإتمام السحب.";
                        }
                        else
                        {
                            var t = new Transaction { UserId = user.Id, AccountId = account.Id, Type = type, Amount = amount, Notes = notes, Date = DateTime.UtcNow, WebManagementId = "Bot" };
                            if (type == "Deposit") account.CurrentBalance += amount;
                            else if (type == "Withdraw") account.CurrentBalance -= amount;
                            context.Transactions.Add(t);
                            await context.SaveChangesAsync();
                            var typeAr = type == "Deposit" ? "إيداع" : "سحب";
                            botReply = $"تمت إضافة {typeAr} بقيمة {amount}$ لحساب {account.Name} بنجاح.";
                        }
                    }
                }
                else if (action == "search_transactions")
                {
                    var query = actionDoc.RootElement.TryGetProperty("query", out var q) ? q.GetString() : "";
                    
                    var txQuery = context.Transactions.Where(t => t.UserId == user.Id);

                    if (!string.IsNullOrEmpty(query))
                    {
                        var matchingAccountIds = context.Accounts
                            .Where(a => a.UserId == user.Id && a.Name.Contains(query))
                            .Select(a => a.Id)
                            .ToList();

                        bool isIdQuery = int.TryParse(query, out int queryId);

                        txQuery = txQuery.Where(t => 
                            (isIdQuery && t.Id == queryId) ||
                            (t.Notes != null && t.Notes.Contains(query)) || 
                            (t.WebManagementId != null && t.WebManagementId.Contains(query)) ||
                            matchingAccountIds.Contains(t.AccountId) ||
                            (t.ToAccountId != null && matchingAccountIds.Contains(t.ToAccountId ?? 0))
                        );
                    }

                    var txs = await txQuery.OrderByDescending(t => t.Date).Take(5).ToListAsync();
                    
                    if (txs.Count == 0) botReply = "لم يتم العثور على أي معاملات.";
                    else
                    {
                        botReply = "المعاملات الأخيرة:\n" + string.Join("\n", txs.Select(t => 
                        {
                            var accName = userAccounts.FirstOrDefault(a => a.Id == t.AccountId)?.Name ?? "غير معروف";
                            var toAccName = t.ToAccountId.HasValue ? (" ➔ " + (userAccounts.FirstOrDefault(a => a.Id == t.ToAccountId.Value)?.Name ?? "غير معروف")) : "";
                            var typeAr = t.Type == "Deposit" ? "إيداع" : t.Type == "Withdraw" ? "سحب" : "تحويل";
                            return $"- {t.Date.ToShortDateString()} | {typeAr} {t.Amount}$ | حساب: {accName}{toAccName} | {t.Notes}";
                        }));
                    }
                }
                else if (action == "get_transaction_details")
                {
                    var query = actionDoc.RootElement.TryGetProperty("query", out var q) ? q.GetString() : "";
                    if (string.IsNullOrEmpty(query)) 
                    {
                        botReply = "يرجى توضيح العملية التي تريد تفاصيلها (مثلاً: تفاصيل عملية الإيجار).";
                    }
                    else
                    {
                        bool isIdQuery = int.TryParse(query, out int queryId);

                        var tx = await context.Transactions
                            .Include(t => t.Attachments)
                            .OrderByDescending(t => t.Date)
                            .FirstOrDefaultAsync(t => t.UserId == user.Id && (
                                (isIdQuery && t.Id == queryId) ||
                                (t.Notes != null && t.Notes.Contains(query)) || 
                                (t.WebManagementId != null && t.WebManagementId.Contains(query)) || 
                                (t.ReferenceNumber != null && t.ReferenceNumber.Contains(query))
                            ));
                            
                        if (tx == null)
                        {
                            botReply = "لم يتم العثور على العملية المطلوبة.";
                        }
                        else
                        {
                            var accName = userAccounts.FirstOrDefault(a => a.Id == tx.AccountId)?.Name ?? "غير معروف";
                            var toAccName = tx.ToAccountId.HasValue ? (" ➔ " + (userAccounts.FirstOrDefault(a => a.Id == tx.ToAccountId.Value)?.Name ?? "غير معروف")) : "";
                            var typeAr = tx.Type == "Deposit" ? "إيداع" : tx.Type == "Withdraw" ? "سحب" : "تحويل";
                            
                            var details = $"تفاصيل العملية:\nالتاريخ: {tx.Date.ToShortDateString()}\nالنوع: {typeAr}\nالمبلغ: {tx.Amount}$\nالحساب: {accName}{toAccName}\n";
                            if (!string.IsNullOrEmpty(tx.Notes)) details += $"الملاحظات: {tx.Notes}\n";
                            if (!string.IsNullOrEmpty(tx.ReferenceNumber)) details += $"رقم المرجع: {tx.ReferenceNumber}\n";
                            
                            await SendTelegramMessage(user.TelegramBotToken, chatId, details);

                            if (!string.IsNullOrEmpty(tx.ReceiptImage))
                            {
                                await SendTelegramPhotoBase64(user.TelegramBotToken, chatId, tx.ReceiptImage);
                            }
                            
                            foreach (var att in tx.Attachments)
                            {
                                if (System.IO.File.Exists(att.FilePath))
                                {
                                    await SendTelegramDocumentFile(user.TelegramBotToken, chatId, att.FilePath);
                                }
                            }
                            
                            botReply = ""; // Empty out to avoid sending another message at the end
                        }
                    }
                }
                else if (action == "get_info")
                {
                    var accs = await context.Accounts.Where(a => a.UserId == user.Id).ToListAsync();
                    if (accs.Count == 0) botReply = "لم يتم العثور على حسابات.";
                    else botReply = "حساباتك:\n" + string.Join("\n", accs.Select(a => $"{a.Name}: {a.CurrentBalance}$"));
                }

                if (!string.IsNullOrEmpty(botReply))
                {
                    await SendTelegramMessage(user.TelegramBotToken, chatId, botReply);
                }
            }
            catch (Exception ex)
            {
                await SendTelegramMessage(user.TelegramBotToken, chatId, "عذراً، لم أتمكن من معالجة هذا الطلب.");
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task SendTelegramMessage(string token, long chatId, string text)
        {
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = new { chat_id = chatId, text = text };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(url, content);
        }

        private async Task SendTelegramPhotoBase64(string token, long chatId, string base64)
        {
            try 
            {
                if (base64.Contains(",")) base64 = base64.Substring(base64.IndexOf(",") + 1);
                var bytes = Convert.FromBase64String(base64);
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(chatId.ToString()), "chat_id");
                var imageContent = new ByteArrayContent(bytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                content.Add(imageContent, "photo", "receipt.jpg");
                
                await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendPhoto", content);
            } 
            catch(Exception ex) { Console.WriteLine("Error sending base64 photo: " + ex.Message); }
        }

        private async Task SendTelegramDocumentFile(string token, long chatId, string filePath)
        {
            try 
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(chatId.ToString()), "chat_id");
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream"); 
                content.Add(fileContent, "document", System.IO.Path.GetFileName(filePath));
                
                await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendDocument", content);
            } 
            catch(Exception ex) { Console.WriteLine("Error sending document file: " + ex.Message); }
        }
    }
}
