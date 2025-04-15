﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Test_API.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Test_API.Controllers
{

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class LoggerResourceFilter : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            Console.WriteLine(" ➡️ Request is Starting ➡️");
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
            Console.WriteLine(" ✅ Request is Completed ✅");
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class InputValidatorFilter : Attribute, IActionFilter
    {
        bool changes = false;
        public void OnActionExecuting(ActionExecutingContext context)
        {
            string spaces = " ";
            if(context.ActionArguments.Any(arg => arg.Value == null))
            {
                context.Result = new BadRequestObjectResult("Input cannot be null.");
                changes= true;
                return;
            }

            if (context.ActionArguments.Any(arg => 
            arg.Value is string str && (str.Contains(spaces) || str != str.Trim()))
            )
            {
                context.Result = new BadRequestObjectResult("Input cannot contain only spaces or have leading/trailing spaces.");
                changes = true;
                return;
            }

            // Validate the model state
            if (!context.ModelState.IsValid)
            {
                context.Result = new BadRequestObjectResult(context.ModelState);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if(changes)
            Console.WriteLine("Input Sanitation Completed ✔️");
        }
    }

    [ApiController]
    [Route("api/Users")]
    
    public class UserController : ControllerBase
    {
        private readonly UserContext _context;
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _configuration;

        public UserController(UserContext context, ILogger<UserController> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet(Name = "GetUserDetails")]
        [LoggerResourceFilter]
        public async Task<IEnumerable<User>> Get()
        {
            return await _context.Users.ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult<User>> Post(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
        }

        [HttpPut("{id}")]
        [LoggerResourceFilter]
        public async Task<IActionResult> Put(int id, User user)
        {
            if (id != user.Id)
            {
                return BadRequest();
            }

            _context.Entry(user).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound();
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return StatusCode(204);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while deleting User Details");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get_By_Id(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound();
                }
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while getting User details with {id}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("count")]
        public async Task<IActionResult> Count_Users()
        {
            try
            {
                var count = await _context.Users.CountAsync();
                return Ok(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while counting Users.");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login()
        {
            try
            {
                
                var loginData = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginRequest>(Request.Body);

                var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == loginData.Email && u.Password == loginData.Password);
                Console.WriteLine($"User : {loginData.Email}, Pass : {loginData.Password}");

                if (user == null)
                {
                    return Unauthorized("Invalid email or password.");
                }

                var token = GenerateJwtToken(user);
                return Ok(new { token });
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Invalid JSON format in Login request.");
                return BadRequest("Invalid JSON format.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on Login");
                return StatusCode(500, "Internal server error");
            }
        }

        public class LoginRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        [HttpPost("SignUp")]
        public async Task<IActionResult> Signup(string name, string email, string password)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.Email == email))
                {
                    return Conflict("A user with this email already exists.");
                }
                var user = new User
                {
                    Name = name,
                    Email = email,
                    Password = password
                };
                await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();
                return CreatedAtAction(nameof(Get_By_Id), new { id = user.Id }, user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on SignUp");
                return StatusCode(500);
            }
        }

        [HttpGet("Header-Test")]
        public ActionResult CustomHeader()
        {
            HttpContext.Response.Headers.Append("x-my-custom-header", "Accepted");
            return Ok();
        }

        private bool UserExists(int id)
        {
            var user = _context.Users.Find(id);
            return user != null;
        }

        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? throw new ArgumentNullException("Jwt:Key", "JWT key is not configured.");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(Convert.ToDouble(_configuration["Jwt:ExpireDays"])),
                signingCredentials: creds);

            Console.WriteLine(token);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}