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
    public class SIPRegistrarBindingsController : ControllerBase
    {
        private readonly SIPAssetsDbContext _context;

        public SIPRegistrarBindingsController(SIPAssetsDbContext context)
        {
            _context = context;
        }

        // GET: api/SIPRegistrarBindings
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SIPRegistrarBinding>>> GetSIPRegistrarBindings()
        {
            return await _context.SIPRegistrarBindings.ToListAsync();
        }

        // GET: api/SIPRegistrarBindings/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SIPRegistrarBinding>> GetSIPRegistrarBinding(Guid id)
        {
            var sIPRegistrarBinding = await _context.SIPRegistrarBindings.FindAsync(id);

            if (sIPRegistrarBinding == null)
            {
                return NotFound();
            }

            return sIPRegistrarBinding;
        }

        // PUT: api/SIPRegistrarBindings/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSIPRegistrarBinding(Guid id, SIPRegistrarBinding sIPRegistrarBinding)
        {
            if (id != sIPRegistrarBinding.ID)
            {
                return BadRequest();
            }

            _context.Entry(sIPRegistrarBinding).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SIPRegistrarBindingExists(id))
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

        // POST: api/SIPRegistrarBindings
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SIPRegistrarBinding>> PostSIPRegistrarBinding(SIPRegistrarBinding sIPRegistrarBinding)
        {
            _context.SIPRegistrarBindings.Add(sIPRegistrarBinding);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SIPRegistrarBindingExists(sIPRegistrarBinding.ID))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetSIPRegistrarBinding", new { id = sIPRegistrarBinding.ID }, sIPRegistrarBinding);
        }

        // DELETE: api/SIPRegistrarBindings/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSIPRegistrarBinding(Guid id)
        {
            var sIPRegistrarBinding = await _context.SIPRegistrarBindings.FindAsync(id);
            if (sIPRegistrarBinding == null)
            {
                return NotFound();
            }

            _context.SIPRegistrarBindings.Remove(sIPRegistrarBinding);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SIPRegistrarBindingExists(Guid id)
        {
            return _context.SIPRegistrarBindings.Any(e => e.ID == id);
        }
    }
}
