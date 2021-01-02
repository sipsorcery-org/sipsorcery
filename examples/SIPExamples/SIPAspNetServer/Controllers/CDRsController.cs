using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using demo.DataAccess;

namespace SIPAspNetServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CDRsController : ControllerBase
    {
        private readonly SIPAssetsDbContext _context;

        public CDRsController(SIPAssetsDbContext context)
        {
            _context = context;
        }

        // GET: api/CDRs
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CDR>>> GetCDRs()
        {
            return await _context.CDRs.ToListAsync();
        }

        // GET: api/CDRs/5
        [HttpGet("{id}")]
        public async Task<ActionResult<CDR>> GetCDR(Guid id)
        {
            var cDR = await _context.CDRs.FindAsync(id);

            if (cDR == null)
            {
                return NotFound();
            }

            return cDR;
        }

        // PUT: api/CDRs/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCDR(Guid id, CDR cDR)
        {
            if (id != cDR.ID)
            {
                return BadRequest();
            }

            _context.Entry(cDR).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CDRExists(id))
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

        // POST: api/CDRs
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<CDR>> PostCDR(CDR cDR)
        {
            _context.CDRs.Add(cDR);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (CDRExists(cDR.ID))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetCDR", new { id = cDR.ID }, cDR);
        }

        // DELETE: api/CDRs/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCDR(Guid id)
        {
            var cDR = await _context.CDRs.FindAsync(id);
            if (cDR == null)
            {
                return NotFound();
            }

            _context.CDRs.Remove(cDR);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CDRExists(Guid id)
        {
            return _context.CDRs.Any(e => e.ID == id);
        }
    }
}
