using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using demo.DataAccess;

namespace demo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SIPDomainsController : ControllerBase
    {
        private readonly SIPAssetsDbContext _context;

        public SIPDomainsController(SIPAssetsDbContext context)
        {
            _context = context;
        }

        // GET: api/SIPDomains
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SIPDomain>>> GetSIPDomains()
        {
            return await _context.SIPDomains.ToListAsync();
        }

        // GET: api/SIPDomains/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SIPDomain>> GetSIPDomain(Guid id)
        {
            var SIPDomain = await _context.SIPDomains.FindAsync(id);

            if (SIPDomain == null)
            {
                return NotFound();
            }

            return SIPDomain;
        }

        // PUT: api/SIPDomains/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSIPDomain(Guid id, SIPDomain SIPDomain)
        {
            if (id != SIPDomain.ID)
            {
                return BadRequest();
            }

            _context.Entry(SIPDomain).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SIPDomainExists(id))
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

        // POST: api/SIPDomains
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SIPDomain>> PostSIPDomain(SIPDomain SIPDomain)
        {
            _context.SIPDomains.Add(SIPDomain);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SIPDomainExists(SIPDomain.ID))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetSIPDomain", new { id = SIPDomain.ID }, SIPDomain);
        }

        // DELETE: api/SIPDomains/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSIPDomain(Guid id)
        {
            var SIPDomain = await _context.SIPDomains.FindAsync(id);
            if (SIPDomain == null)
            {
                return NotFound();
            }

            _context.SIPDomains.Remove(SIPDomain);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SIPDomainExists(Guid id)
        {
            return _context.SIPDomains.Any(e => e.ID == id);
        }
    }
}
