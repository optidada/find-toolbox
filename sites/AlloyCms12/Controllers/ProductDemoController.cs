using Microsoft.AspNetCore.Mvc;
using ToolboxTestHarness;
using ToolboxTestHarness.ProductDemo;

namespace AlloyCms12.Controllers;

/// <summary>
/// Faceted product search demo powered by the Find Toolbox: /product-demo?q=laptop&amp;brand=Fabrikam
/// (data seeded by POST /toolbox-test/seed).
/// </summary>
[Route("product-demo")]
public class ProductDemoController : Controller
{
    private readonly ToolboxTestService _service;

    public ProductDemoController(ToolboxTestService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ProductSearchRequest request)
        => View("~/Views/ProductDemo/Index.cshtml", await _service.ProductSearchAsync(request));
}
