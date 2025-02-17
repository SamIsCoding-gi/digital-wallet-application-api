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

         // testing db connection
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

        // creates account and enters users info and creates wallet entry
        [HttpPost("create-account")]
        public async Task<IActionResult> CreateAccount([FromBody] User user)
        {
            if (user.Password != user.ConfirmPassword)
            {
                _logger.LogWarning("Passwords do not match.");
                return BadRequest("Passwords do not match.");
            }

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == user.Email || u.PhoneNumber == user.PhoneNumber);

            if (existingUser != null)
            {
                _logger.LogWarning("Email or phone number already exists.");
                return BadRequest("Email or phone number already exists.");
            }

            try
            {
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Account created successfully for user {UserId}", user.Id);

                string createWalletTableSql = @"
            CREATE TABLE IF NOT EXISTS `Wallet` (
                WalletId CHAR(36) PRIMARY KEY,
                UserId CHAR(36) NOT NULL,
                Balance DECIMAL(18,2) NOT NULL,
                CreatedDate DATETIME
            );";
                await _context.Database.ExecuteSqlRawAsync(createWalletTableSql);

                var walletId = Guid.NewGuid();
                decimal initialWalletBalance = 500; // Initial wallet balance for testing purposes

                string insertWalletSql = $@"
            INSERT INTO `Wallet`
            (WalletId, UserId, Balance, CreatedDate)
            VALUES 
            ('{walletId}', '{user.Id}', {initialWalletBalance}, NOW());";
                await _context.Database.ExecuteSqlRawAsync(insertWalletSql);

                return Ok(new { message = "Account created successfully", userId = user.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create account.");
                return StatusCode(500, $"Failed to create account: {ex.Message}");
            }
        }

         // signs in a user based on email and password
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
            };
            return Ok(new { success = true, user = userDto });
        }

        // fetches current user data
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
            };

            return Ok(userDto);
        }

        //searches for users based pm email
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

        // transfers funds from one user to another and registers them
        // in transactions
        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferRequest request)
        {
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

            var recipient = await _context.Users.FindAsync(request.RecipientId);
            if (recipient == null)
            {
                _logger.LogWarning("Recipient not found.");
                return NotFound("Recipient not found.");
            }

            decimal senderWalletBalance = 0;
            decimal recipientWalletBalance = 0;

            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"SELECT Balance FROM Wallet WHERE UserId = '{sender.Id}'";
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        senderWalletBalance = Convert.ToDecimal(result);
                    }
                    else
                    {
                        return NotFound("Sender wallet not found.");
                    }
                }

                if (senderWalletBalance < request.AmountToSend)
                {
                    _logger.LogWarning("Insufficient funds for sender: {UserId}", sender.Id);
                    return BadRequest("Insufficient funds.");
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"SELECT Balance FROM Wallet WHERE UserId = '{recipient.Id}'";
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        recipientWalletBalance = Convert.ToDecimal(result);
                    }
                    else
                    {
                        return NotFound("Recipient wallet not found.");
                    }
                }

                decimal newSenderBalance = senderWalletBalance - request.AmountToSend;
                decimal newRecipientBalance = recipientWalletBalance + request.AmountToSend;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"UPDATE Wallet SET Balance = {newSenderBalance} WHERE UserId = '{sender.Id}'";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"UPDATE Wallet SET Balance = {newRecipientBalance} WHERE UserId = '{recipient.Id}'";
                    await cmd.ExecuteNonQueryAsync();
                }

                string createTableSql = @"
            CREATE TABLE IF NOT EXISTS `Transactions` (
                TransactionId CHAR(36) PRIMARY KEY,
                UserId CHAR(36) NOT NULL,
                CounterPartyFirstName VARCHAR(100),
                CounterPartyLastName VARCHAR(100),
                TransactionDate DATETIME,
                Amount DECIMAL(18,2),
                Type VARCHAR(50)
            );";
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = createTableSql;
                    await cmd.ExecuteNonQueryAsync();
                }

                var debitTransactionId = Guid.NewGuid();
                string insertDebitSql = $@"
            INSERT INTO `Transactions`
            (TransactionId, UserId, CounterPartyFirstName, CounterPartyLastName, TransactionDate, Amount, Type)
            VALUES 
            ('{debitTransactionId}', '{sender.Id}', '{recipient.FirstName}', '{recipient.LastName}', NOW(), {request.AmountToSend}, 'debit');";
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = insertDebitSql;
                    await cmd.ExecuteNonQueryAsync();
                }

                var creditTransactionId = Guid.NewGuid();
                string insertCreditSql = $@"
            INSERT INTO `Transactions`
            (TransactionId, UserId, CounterPartyFirstName, CounterPartyLastName, TransactionDate, Amount, Type)
            VALUES 
            ('{creditTransactionId}', '{recipient.Id}', '{sender.FirstName}', '{sender.LastName}', NOW(), {request.AmountToSend}, 'credit');";
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = insertCreditSql;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await connection.CloseAsync();
            }

            _logger.LogInformation("Transfer successful: {Amount} from {SenderId} to {RecipientId}", request.AmountToSend, sender.Id, recipient.Id);
            return Ok(new { success = true, message = "Transfer successful." });
        }

        // fetches transaction history
        [HttpGet("transactionHistory/{userId}")]
        public async Task<IActionResult> GetTransactionHistory(Guid userId)
        {
            try
            {
                var transactions = new List<TransactionRecord>();
                string sql = $@"
            SELECT TransactionId, UserId, CounterPartyFirstName, CounterPartyLastName, TransactionDate, Amount, Type
            FROM `Transactions`
            WHERE UserId = '{userId}'
            ORDER BY TransactionDate DESC";

                using (var connection = _context.Database.GetDbConnection())
                {
                    await connection.OpenAsync();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var transaction = new TransactionRecord
                        {
                            TransactionId = Guid.Parse(reader["TransactionId"].ToString()),
                            UserId = Guid.Parse(reader["UserId"].ToString()),
                            CounterPartyFirstName = reader["CounterPartyFirstName"].ToString(),
                            CounterPartyLastName = reader["CounterPartyLastName"].ToString(),
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

        // fetches wallet data
        [HttpGet("wallet/{userId}")]
        public async Task<IActionResult> GetWalletBalance(Guid userId)
        {
            try
            {
                decimal balance = 0;
                using (var connection = _context.Database.GetDbConnection())
                {
                    await connection.OpenAsync();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT Balance FROM Wallet WHERE UserId = '{userId}'";
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        balance = Convert.ToDecimal(result);
                    }
                    else
                    {
                        return NotFound("Wallet not found for user.");
                    }
                }
                return Ok(new { UserId = userId, Balance = balance });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching wallet balance for user {UserId}", userId);
                return StatusCode(500, "Unable to fetch wallet balance.");
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
        public Guid UserId { get; set; }
        public string CounterPartyFirstName { get; set; }
        public string CounterPartyLastName { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; }
    }
}