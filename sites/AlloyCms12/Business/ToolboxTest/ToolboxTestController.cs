using Microsoft.AspNetCore.Mvc;
using ToolboxTestHarness;

namespace AlloyCms12.Business.ToolboxTest;

/// <summary>
/// Test-only endpoints used by tests/Optidada.SearchNavigation.Toolbox.IntegrationTests.
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

    /// <param name="area">pages | media | blocks</param>
    [HttpGet("provider")]
    public IActionResult Provider([FromQuery] string area, [FromQuery] string q, [FromServices] IServiceProvider services)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        EPiServer.Shell.Search.ISearchProvider provider = area switch
        {
            "media" => ActivatorUtilities.CreateInstance<Optidada.SearchNavigation.Toolbox.SearchProviders.ToolboxMediaSearchProvider>(services),
            "blocks" => ActivatorUtilities.CreateInstance<Optidada.SearchNavigation.Toolbox.SearchProviders.ToolboxBlockSearchProvider>(services),
            _ => ActivatorUtilities.CreateInstance<Optidada.SearchNavigation.Toolbox.SearchProviders.ToolboxPageSearchProvider>(services),
        };

        return Ok(_service.SearchWithProvider(provider, q));
    }

    /// <summary>Indexes all site content (the Alloy demo pages) so the search page has content.</summary>
    [HttpPost("index-site")]
    public async Task<IActionResult> IndexSite() => _environment.IsDevelopment() ? Ok(await _service.IndexSiteAsync()) : NotFound();

    /// <summary>Faceted product search as JSON (same as /product-demo).</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] ToolboxTestHarness.ProductDemo.ProductSearchRequest request)
        => _environment.IsDevelopment() ? Ok(await _service.ProductSearchAsync(request)) : NotFound();

    [HttpGet("count")]
    public async Task<IActionResult> Count() => _environment.IsDevelopment() ? Ok(await _service.CountAsync()) : NotFound();

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ToolboxSearchRequest request) => _environment.IsDevelopment() ? Ok(await _service.SearchAsync(request)) : NotFound();
}
