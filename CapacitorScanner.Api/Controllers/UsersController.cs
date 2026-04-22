using CapacitorScanner.Api.Models;
using CapacitorScanner.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CapacitorScanner.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private BinLocalDbService dbService;
        public UsersController(BinLocalDbService dbService)
        {
            this.dbService = dbService;
        }
        [HttpPut("{username}")]
        public async Task<IActionResult> updatepassword(string username,UpdatePasswordRequestModel req)
        {
            return await dbService.UpdatePasswordLogin(username, req.OldPassword, req.NewPassword) ? Ok() : NotFound("User not found");
        }
    }
}
