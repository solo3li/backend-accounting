using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace backend.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string TelegramBotToken { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;
        public long LastTelegramUpdateId { get; set; } = 0;
    }

    public class Account
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Instapay, Bank Account, Vodafone Cash, etc.
        public decimal InitialBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int AccountId { get; set; }
        public int? ToAccountId { get; set; } // For transfers
        public string Type { get; set; } = string.Empty; // Deposit, Withdraw, Transfer
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string Notes { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string ReceiptImage { get; set; } = string.Empty; // Base64 image
        public string WebManagementId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<TransactionTag> TransactionTags { get; set; } = new();
        public List<Attachment> Attachments { get; set; } = new();
    }

    public class Tag
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class TransactionTag
    {
        public int TransactionId { get; set; }
        public int TagId { get; set; }
        public Tag? Tag { get; set; }
    }

    public class Attachment
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
