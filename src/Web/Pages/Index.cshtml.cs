using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.Web.Messaging;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.eShopWeb.Web.ViewModels;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ICatalogViewModelService _catalogViewModelService;
    private readonly InventoryAvailabilityService _inventory;
    private readonly IOptions<StockCheckOptions> _stockCheck;

    public IndexModel(ICatalogViewModelService catalogViewModelService,
        InventoryAvailabilityService inventory,
        IOptions<StockCheckOptions> stockCheck)
    {
        _catalogViewModelService = catalogViewModelService;
        _inventory = inventory;
        _stockCheck = stockCheck;
    }

    public required CatalogIndexViewModel CatalogModel { get; set; } = new CatalogIndexViewModel();

    // Catalog item Id → "In stock: 5", "Out of stock" or "Availability unknown". Empty when StockCheck is off.
    public Dictionary<int, string> Availability { get; } = new();

    public async Task OnGet(CatalogIndexViewModel catalogModel, int? pageId)
    {
        CatalogModel = await _catalogViewModelService.GetCatalogItems(pageId ?? 0, Constants.ITEMS_PER_PAGE, catalogModel.BrandFilterApplied, catalogModel.TypesFilterApplied);

        if (_stockCheck.Value.Enabled)
        {
            var items = CatalogModel.CatalogItems;
            var results = await Task.WhenAll(items.Select(item => _inventory.GetAvailabilityAsync(item.Id, HttpContext.RequestAborted)));
            for (var i = 0; i < items.Count; i++)
            {
                Availability[items[i].Id] = results[i];
            }
        }
    }
}
