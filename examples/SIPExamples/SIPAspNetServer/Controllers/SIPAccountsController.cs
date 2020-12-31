using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPAspNetServer.DataAccess;

namespace SIPAspNetServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SIPAccountsController : ControllerBase
    {
        private readonly SIPAssetsDbContext _context;

        public SIPAccountsController(SIPAssetsDbContext context)
        {
            _context = context;
        }

        // GET: api/SIPAccounts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SIPAccount>>> GetSIPAccounts()
        {
            return await _context.SIPAccounts.ToListAsync();
        }

        // GET: api/SIPAccounts/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SIPAccount>> GetSIPAccount(Guid id)
        {
            var SIPAccount = await _context.SIPAccounts.FindAsync(id);

            if (SIPAccount == null)
            {
                return NotFound();
            }

            return SIPAccount;
        }

        // PUT: api/SIPAccounts/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSIPAccount(Guid id, SIPAccount SIPAccount)
        {
            if (id != SIPAccount.ID)
            {
                return BadRequest();
            }

            _context.Entry(SIPAccount).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SIPAccountExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/SIPAccounts
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SIPAccount>> PostSIPAccount(SIPAccount SIPAccount)
        {
            _context.SIPAccounts.Add(SIPAccount);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SIPAccountExists(SIPAccount.ID))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetSIPAccount", new { id = SIPAccount.ID }, SIPAccount);
        }

        // DELETE: api/SIPAccounts/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSIPAccount(Guid id)
        {
            var SIPAccount = await _context.SIPAccounts.FindAsync(id);
            if (SIPAccount == null)
            {
                return NotFound();
            }

            _context.SIPAccounts.Remove(SIPAccount);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SIPAccountExists(Guid id)
        {
            return _context.SIPAccounts.Any(e => e.ID == id);
        }
    }
}
