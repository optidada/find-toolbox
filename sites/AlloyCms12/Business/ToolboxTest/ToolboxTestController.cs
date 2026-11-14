using Microsoft.AspNetCore.Mvc;
using ToolboxTestHarness;

namespace AlloyCms12.Business.ToolboxTest;

/// <summary>
/// Test-only endpoints used by tests/EPiServer.Labs.Find.Toolbox.IntegrationTests.
/// Only mapped when the site runs in the Development environment.
/// </summary>
[ApiController]
[Route("toolbox-test")]
public class ToolboxTestController : ControllerBase
{
    private readonly ToolboxTestService _service;
    private readonly IWebHostEnvironment _environment;

    public ToolboxTestController(ToolboxTestService service, IWebHostEnvironment environment)
    {
        _service = service;
        _environment = environment;
    }

    /// <param name="resetIndex">Deletes ALL documents and synonyms in the index first. Dedicated test indexes only.</param>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed([FromQuery] bool resetIndex = false) => _environment.IsDevelopment() ? Ok(await _service.SeedAsync(resetIndex)) : NotFound();

    [HttpGet("count")]
    public async Task<IActionResult> Count() => _environment.IsDevelopment() ? Ok(await _service.CountAsync()) : NotFound();

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ToolboxSearchRequest request) => _environment.IsDevelopment() ? Ok(await _service.SearchAsync(request)) : NotFound();
}
