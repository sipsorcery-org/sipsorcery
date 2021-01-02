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
    public class SIPCallsController : ControllerBase
    {
        private readonly SIPAssetsDbContext _context;

        public SIPCallsController(SIPAssetsDbContext context)
        {
            _context = context;
        }

        // GET: api/SIPCalls
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SIPCall>>> GetSIPCalls()
        {
            return await _context.SIPCalls.ToListAsync();
        }

        // GET: api/SIPCalls/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SIPCall>> GetSIPCall(Guid id)
        {
            var sIPCall = await _context.SIPCalls.FindAsync(id);

            if (sIPCall == null)
            {
                return NotFound();
            }

            return sIPCall;
        }

        // PUT: api/SIPCalls/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSIPCall(Guid id, SIPCall sIPCall)
        {
            if (id != sIPCall.ID)
            {
                return BadRequest();
            }

            _context.Entry(sIPCall).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SIPCallExists(id))
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

        // POST: api/SIPCalls
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SIPCall>> PostSIPCall(SIPCall sIPCall)
        {
            _context.SIPCalls.Add(sIPCall);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SIPCallExists(sIPCall.ID))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetSIPCall", new { id = sIPCall.ID }, sIPCall);
        }

        // DELETE: api/SIPCalls/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSIPCall(Guid id)
        {
            var sIPCall = await _context.SIPCalls.FindAsync(id);
            if (sIPCall == null)
            {
                return NotFound();
            }

            _context.SIPCalls.Remove(sIPCall);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SIPCallExists(Guid id)
        {
            return _context.SIPCalls.Any(e => e.ID == id);
        }
    }
}
