using Microsoft.AspNetCore.Mvc;
using digital_wallet_application_api;
using digital_wallet_application_api.Models.Entities;
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

            user.Balance = 500; // Initialize balance to 500

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
            return Ok(new { success = true, user });
        }
    }

    public class SignInRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}