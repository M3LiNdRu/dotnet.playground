using ApplicationServices.Modules;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebApp
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private readonly IEnumerable<string> _values;

        private readonly ICommonModule _commonModule;

        public ValuesController(ICommonModule commonModule)
        {
            _values = new List<string> { "value1", "value2" };
            _commonModule = commonModule;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            _commonModule.UpdateTimestamp();
            return Ok(_values);
        }
    }
}
