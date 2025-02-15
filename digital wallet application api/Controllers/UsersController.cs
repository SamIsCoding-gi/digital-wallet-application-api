using Microsoft.AspNetCore.Mvc;
using digital_wallet_application_api;
using digital_wallet_application_api.Models.Entities;
using digital_wallet_application_api.Models.DTOs;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace digital_wallet_application_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UsersController> _logger;

        public UsersController(ApplicationDbContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                await _context.Database.OpenConnectionAsync();
                await _context.Database.CloseConnectionAsync();
                return Ok("Database connection successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection failed.");
                return StatusCode(500, $"Database connection failed: {ex.Message}");
            }
        }

        [HttpPost("create-account")]
        public async Task<IActionResult> CreateAccount([FromBody] User user)
        {
            if (user.Password != user.ConfirmPassword)
            {
                _logger.LogWarning("Passwords do not match.");
                return BadRequest("Passwords do not match.");
            }

            // Check if email or phone number already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == user.Email || u.PhoneNumber == user.PhoneNumber);

            if (existingUser != null)
            {
                _logger.LogWarning("Email or phone number already exists.");
                return BadRequest("Email or phone number already exists.");
            }

            user.Balance = 500; // Initialize balance to 500 for testing

            try
            {
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Account created successfully for user {UserId}", user.Id);
                return Ok(new { message = "Account created successfully", userId = user.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create account.");
                return StatusCode(500, $"Failed to create account: {ex.Message}");
            }
        }

        [HttpPost("signin")]
        public async Task<IActionResult> SignIn([FromBody] SignInRequest request)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email && u.Password == request.Password);

            if (user == null)
            {
                _logger.LogWarning("Invalid email or password.");
                return BadRequest("Invalid email or password.");
            }

            _logger.LogInformation("User signed in successfully: {UserId}", user.Id);
            var userDto = new UserDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Balance = user.Balance
            };
            return Ok(new { success = true, user = userDto });
        }

        [HttpGet("userData/{userId}")]
        public async Task<IActionResult> GetUserById(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var userDto = new UserDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Balance = user.Balance
            };

            return Ok(userDto);
        }

        [HttpGet("search/{email}")]
        public async Task<IActionResult> SearchUserByEmail(string email, [FromQuery] Guid? excludeId)
        {
            var users = await _context.Users
                .Where(u => EF.Functions.Like(u.Email.ToLower(), $"{email.ToLower()}%"))
                .Where(u => !excludeId.HasValue || u.Id != excludeId.Value)
                .ToListAsync();

            if (users == null || !users.Any())
            {
                _logger.LogWarning("No users with email like {Email} found.", email);
                return NotFound("User not found.");
            }

            var userSearchDtos = users.Select(user => new UserSearchDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber
            });

            return Ok(userSearchDtos);
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferRequest request)
        {
            // Find the sender and validate password and balance
            var sender = await _context.Users.FindAsync(request.UserId);
            if (sender == null)
            {
                _logger.LogWarning("Sender not found.");
                return NotFound("Sender not found.");
            }

            if (sender.Password != request.Password)
            {
                _logger.LogWarning("Invalid password for sender: {UserId}", sender.Id);
                return BadRequest("Invalid password.");
            }

            if (sender.Balance < request.AmountToSend)
            {
                _logger.LogWarning("Insufficient funds for sender: {UserId}", sender.Id);
                return BadRequest("Insufficient funds.");
            }

            // Find the recipient using the provided recipientId.
            var recipient = await _context.Users.FindAsync(request.RecipientId);
            if (recipient == null)
            {
                _logger.LogWarning("Recipient not found.");
                return NotFound("Recipient not found.");
            }

            // Update balances
            sender.Balance -= request.AmountToSend;
            recipient.Balance += request.AmountToSend;

            await _context.SaveChangesAsync();

            // Define table names for transactions (remove hyphens from GUID strings)
            string senderTable = $"Transactions_{sender.Id.ToString().Replace("-", "")}";
            string recipientTable = $"Transactions_{recipient.Id.ToString().Replace("-", "")}";

            // Create sender transaction table if it doesn't exist (MySQL syntax)
            string createSenderTableSql = $@"
             CREATE TABLE IF NOT EXISTS `{senderTable}` (
             TransactionId CHAR(36) PRIMARY KEY,
             FirstName VARCHAR(100),
             LastName VARCHAR(100),
             Email VARCHAR(100),
             PhoneNumber VARCHAR(50),
             TransactionDate DATETIME,
             Amount DECIMAL(18,2),
             Type VARCHAR(50)
             );";
            await _context.Database.ExecuteSqlRawAsync(createSenderTableSql);

            // Create recipient transaction table if it doesn't exist (MySQL syntax)
            string createRecipientTableSql = $@"
             CREATE TABLE IF NOT EXISTS `{recipientTable}` (
             TransactionId CHAR(36) PRIMARY KEY,
             FirstName VARCHAR(100),
             LastName VARCHAR(100),
             Email VARCHAR(100),
             PhoneNumber VARCHAR(50),
             TransactionDate DATETIME,
             Amount DECIMAL(18,2),
             Type VARCHAR(50)
             );";
            await _context.Database.ExecuteSqlRawAsync(createRecipientTableSql);

            // Insert a transaction record for the sender (credit)
            var senderTransactionId = Guid.NewGuid();
            string insertSenderSql = $@"
            INSERT INTO `{senderTable}` 
            (TransactionId, FirstName, LastName, Email, PhoneNumber, TransactionDate, Amount, Type)
            VALUES 
            ('{senderTransactionId}', '{sender.FirstName}', '{sender.LastName}', '{sender.Email}', '{sender.PhoneNumber}', NOW(), {request.AmountToSend}, 'credit');";
            await _context.Database.ExecuteSqlRawAsync(insertSenderSql);

            // Insert a transaction record for the recipient (debit)
            var recipientTransactionId = Guid.NewGuid();
            string insertRecipientSql = $@"
            INSERT INTO `{recipientTable}` 
            (TransactionId, FirstName, LastName, Email, PhoneNumber, TransactionDate, Amount, Type)
            VALUES 
            ('{recipientTransactionId}', '{recipient.FirstName}', '{recipient.LastName}', '{recipient.Email}', '{recipient.PhoneNumber}', NOW(), {request.AmountToSend}, 'debit');";
            await _context.Database.ExecuteSqlRawAsync(insertRecipientSql);

            _logger.LogInformation("Transfer successful: {Amount} from {SenderId} to {RecipientId}", request.AmountToSend, sender.Id, recipient.Id);
            return Ok(new { success = true, message = "Transfer successful." });
        }

        [HttpGet("transactionHistory/{userId}")]
        public async Task<IActionResult> GetTransactionHistory(Guid userId)
        {
            try
            {
                // Build dynamic transaction table name for the user
                string tableName = $"Transactions_{userId.ToString().Replace("-", "")}";
                var transactions = new List<TransactionRecord>();

                using (var connection = _context.Database.GetDbConnection())
                {
                    await connection.OpenAsync();

                    // Check if the transaction table exists in the current database.
                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}'";
                        var existsResult = await checkCmd.ExecuteScalarAsync();
                        int tableCount = Convert.ToInt32(existsResult);
                        if (tableCount == 0)
                        {
                            // If the table does not exist, return an empty list.
                            return Ok(transactions);
                        }
                    }

                    // Fetch transactions from the dynamic transaction table
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT TransactionId, FirstName, LastName, Email, PhoneNumber, TransactionDate, Amount, Type FROM `{tableName}` ORDER BY TransactionDate DESC";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var transaction = new TransactionRecord
                        {
                            TransactionId = Guid.Parse(reader["TransactionId"].ToString()),
                            FirstName = reader["FirstName"].ToString(),
                            LastName = reader["LastName"].ToString(),
                            Email = reader["Email"].ToString(),
                            PhoneNumber = reader["PhoneNumber"].ToString(),
                            TransactionDate = Convert.ToDateTime(reader["TransactionDate"]),
                            Amount = Convert.ToDecimal(reader["Amount"]),
                            Type = reader["Type"].ToString()
                        };
                        transactions.Add(transaction);
                    }
                }

                return Ok(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching transaction history for user {UserId}", userId);
                return StatusCode(500, "Unable to fetch transaction history.");
            }
        }
    }

    public class SignInRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class TransferRequest
    {
        public decimal AmountToSend { get; set; }
        public Guid UserId { get; set; }
        public string Password { get; set; }
        public Guid RecipientId { get; set; }
    }

    public class TransactionRecord
    {
        public Guid TransactionId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; }
    }
}