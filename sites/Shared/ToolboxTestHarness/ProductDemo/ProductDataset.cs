using System.Collections.Generic;
using System.Linq;

namespace ToolboxTestHarness.ProductDemo
{
    /// <summary>
    /// Demo products for the faceted product search (/product-demo). No CMS / Find dependencies: shared by both sites
    /// and the integration tests.
    /// </summary>
    public static class ProductDataset
    {
        public const string ContainerName = "Toolbox product demo";

        /// <summary>Swedish products (Swedish language pages, indexed into the .sv fields).</summary>
        public const string SwedishContainerName = "Toolbox product demo (sv)";
        public const string SwedishLanguage = "sv";

        public class Product
        {
            public Product(string name, string category, string brand, string color, double price, string description)
            {
                Name = name; Category = category; Brand = brand; Color = color; Price = price; Description = description;
            }

            public string Name { get; }
            public string Category { get; }
            public string Brand { get; }
            public string Color { get; }
            public double Price { get; }
            public string Description { get; }
        }

        /// <summary>Price facet ranges (from inclusive, to exclusive; null = open).</summary>
        public static readonly (string Key, double? From, double? To)[] PriceRanges =
        {
            ("0-200", null, 200),
            ("200-500", 200, 500),
            ("500-1000", 500, 1000),
            ("1000+", 1000, null),
        };

        /// <summary>Price range keys in order (plain strings: usable from the CMS 11 view compiler, which has no tuple names).</summary>
        public static List<string> PriceRangeKeys => PriceRanges.Select(r => r.Key).ToList();

        public static string PriceRange(double price) => PriceRanges.First(r => (r.From == null || price >= r.From) && (r.To == null || price < r.To)).Key;

        public static readonly Product[] Products =
        {
            // Laptops: half of them are called "notebook" (synonym laptop <=> notebook)
            new Product("Contoso UltraBook 14 Laptop", "Laptops", "Contoso", "Silver", 1299, "Thin and light laptop with all day battery."),
            new Product("Fabrikam Pro Notebook 16", "Laptops", "Fabrikam", "Black", 1899, "Powerful notebook for creators and developers."),
            new Product("Northwind Air Laptop 13", "Laptops", "Northwind", "Silver", 999, "Fanless laptop, perfect for travel."),
            new Product("Litware Gaming Laptop 17", "Laptops", "Litware", "Black", 2199, "High refresh rate gaming laptop."),
            new Product("Tailspin Student Notebook", "Laptops", "Tailspin", "Blue", 449, "Affordable notebook for school work."),
            new Product("Contoso Business Laptop 15", "Laptops", "Contoso", "Black", 1099, "Secure laptop for the office."),
            new Product("Fabrikam Convertible Notebook", "Laptops", "Fabrikam", "White", 849, "Touch screen notebook that folds into a tablet."),
            new Product("Northwind Creator Notebook", "Laptops", "Northwind", "Silver", 1599, "Color accurate display for photo editing."),

            // Phones: "smartphone" and "mobile phone" (synonym mobile phone <=> smartphone)
            new Product("Contoso Smartphone X", "Phones", "Contoso", "Black", 899, "Flagship smartphone with triple camera."),
            new Product("Fabrikam Mobile Phone Lite", "Phones", "Fabrikam", "Blue", 249, "Simple mobile phone with great battery life."),
            new Product("Northwind Smartphone Pro", "Phones", "Northwind", "Silver", 1099, "Pro smartphone with a stunning display."),
            new Product("Litware Rugged Mobile Phone", "Phones", "Litware", "Black", 399, "Waterproof mobile phone for outdoor work."),
            new Product("Tailspin Smartphone Mini", "Phones", "Tailspin", "Red", 499, "Compact smartphone that fits any pocket."),
            new Product("Contoso Smartphone Max", "Phones", "Contoso", "White", 1199, "Big screen smartphone with stylus."),
            new Product("Fabrikam Flip Phone", "Phones", "Fabrikam", "Red", 129, "Classic flip phone with physical keys."),
            new Product("Northwind Mobile Phone Classic", "Phones", "Northwind", "Black", 179, "Easy to use mobile phone for seniors."),

            // Headphones: two "earbuds" (synonym earbuds => headphones) and one headset
            new Product("Contoso Wireless Headphones", "Headphones", "Contoso", "Black", 199, "Comfortable wireless headphones."),
            new Product("Fabrikam Noise Cancelling Headphones", "Headphones", "Fabrikam", "Silver", 349, "Active noise cancelling headphones for travel."),
            new Product("Northwind Sport Earbuds", "Headphones", "Northwind", "Blue", 129, "Sweat resistant earbuds for running."),
            new Product("Litware Studio Headphones", "Headphones", "Litware", "Black", 299, "Studio monitor headphones with flat response."),
            new Product("Tailspin Kids Headphones", "Headphones", "Tailspin", "Red", 39, "Volume limited headphones for children."),
            new Product("Contoso True Wireless Earbuds", "Headphones", "Contoso", "White", 179, "Tiny earbuds with charging case."),
            new Product("Fabrikam Over Ear Headphones", "Headphones", "Fabrikam", "Blue", 149, "Over ear headphones with deep bass."),
            new Product("Northwind Gaming Headset", "Headphones", "Northwind", "Black", 89, "Gaming headset with boom microphone."),

            // TVs: "TV" and "Television" (synonym tv <=> television)
            new Product("Contoso 55 inch 4K TV", "TVs", "Contoso", "Black", 699, "Bright 4K TV with HDR."),
            new Product("Fabrikam OLED Television 65", "TVs", "Fabrikam", "Black", 2499, "OLED television with perfect blacks."),
            new Product("Northwind Smart TV 43", "TVs", "Northwind", "Silver", 399, "Smart TV with streaming apps."),
            new Product("Litware QLED Television 75", "TVs", "Litware", "Silver", 1799, "Large QLED television for the living room."),
            new Product("Tailspin Kitchen TV 24", "TVs", "Tailspin", "White", 179, "Small TV for the kitchen."),
            new Product("Contoso 8K Television 85", "TVs", "Contoso", "Black", 4999, "Huge 8K television."),
            new Product("Fabrikam Outdoor TV 50", "TVs", "Fabrikam", "Black", 1499, "Weatherproof TV for the patio."),
            new Product("Northwind Portable TV 32", "TVs", "Northwind", "Red", 249, "Portable TV with built in battery."),

            // Cameras
            new Product("Contoso Mirrorless Camera", "Cameras", "Contoso", "Black", 1299, "Full frame mirrorless camera."),
            new Product("Fabrikam Action Camera 4K", "Cameras", "Fabrikam", "Black", 299, "Waterproof 4K action camera."),
            new Product("Northwind Instant Camera", "Cameras", "Northwind", "Blue", 89, "Instant camera that prints photos."),
            new Product("Litware DSLR Camera", "Cameras", "Litware", "Black", 899, "Classic DSLR camera with optical viewfinder."),
            new Product("Tailspin Kids Camera", "Cameras", "Tailspin", "Red", 49, "Shockproof camera for kids."),
            new Product("Contoso Compact Camera", "Cameras", "Contoso", "Silver", 449, "Pocket sized compact camera with zoom."),
            new Product("Fabrikam Vlogging Camera", "Cameras", "Fabrikam", "White", 699, "Camera with flip screen for vlogging."),
            new Product("Northwind 360 Camera", "Cameras", "Northwind", "Silver", 399, "360 degree camera for virtual tours."),
        };

        /// <summary>
        /// Swedish products with compound words. Find's Swedish analyzer decompounds at index time (the .sv fields), so
        /// searching for a part of the compound ("kopp", "maskin") can find the compound ("Kaffekopp", "Diskmaskin").
        /// </summary>
        public static readonly Product[] SwedishProducts =
        {
            new Product("Kaffekopp i porslin", "Kök", "Contoso", "White", 99, "Klassisk kaffekopp som tål diskmaskin."),
            new Product("Kaffebryggare Deluxe", "Kök", "Fabrikam", "Black", 899, "Kaffebryggare med termoskanna och timer."),
            new Product("Tekopp med fat", "Kök", "Northwind", "Blue", 79, "Tekopp i stengods med matchande fat."),
            new Product("Vattenkokare i stål", "Kök", "Litware", "Silver", 349, "Snabb vattenkokare med automatisk avstängning."),
            new Product("Datorväska 15 tum", "Datortillbehör", "Tailspin", "Black", 499, "Vadderad datorväska med axelrem."),
            new Product("Bärbar dator 14 tum", "Datorer", "Contoso", "Silver", 8999, "Lätt bärbar dator för studier och arbete."),
            new Product("Trådlösa hörlurar", "Ljud", "Fabrikam", "Black", 1299, "Hörlurar med brusreducering."),
            new Product("Mobilskal i läder", "Mobiltillbehör", "Northwind", "Red", 249, "Skal som skyddar mobilen."),
            new Product("Barnkamera vattentät", "Kameror", "Tailspin", "Blue", 599, "Tålig kamera för barn."),
            new Product("Tvättmaskin 8 kg", "Vitvaror", "Litware", "White", 6999, "Energisnål tvättmaskin med ångprogram."),
            new Product("Diskmaskin integrerad", "Vitvaror", "Contoso", "White", 5999, "Tyst diskmaskin för inbyggnad."),
            new Product("Skrivbordslampa LED", "Belysning", "Fabrikam", "Black", 399, "Dimbar skrivbordslampa med USB-laddning."),
        };

        /// <summary>Synonyms used by the product demo (seeded together with the test synonyms).</summary>
        public static readonly TestDataset.TestSynonym[] Synonyms =
        {
            new TestDataset.TestSynonym("laptop", "notebook", true),
            new TestDataset.TestSynonym("tv", "television", true),
            new TestDataset.TestSynonym("mobile phone", "smartphone", true),
            new TestDataset.TestSynonym("earbuds", "headphones", false),
        };
    }

    /// <summary>Request for the product search (query string of /product-demo and /toolbox-test/products).</summary>
    public class ProductSearchRequest
    {
        public string Q { get; set; }
        /// <summary>Selected facet values; several values within a facet are OR'ed, facets are AND'ed.</summary>
        public List<string> Category { get; set; } = new List<string>();
        public List<string> Brand { get; set; } = new List<string>();
        public List<string> Color { get; set; } = new List<string>();
        /// <summary>Price range keys, see <see cref="ProductDataset.PriceRanges"/>.</summary>
        public List<string> Price { get; set; } = new List<string>();
        /// <summary>
        /// Search language: en (default) | sv | none. Picks the language specific field suffix (.en / .sv) and analyzer
        /// (stemming, decompounding) Find uses for the search.
        /// </summary>
        public string Lang { get; set; } = "en";

        /// <summary>toolbox (default) | find (plain Find: For + built-in UsingSynonyms, OR) | find-and (the same with AND)</summary>
        public string Engine { get; set; } = "toolbox";
        public string Msm { get; set; } = "2<-25%";
        public bool Synonyms { get; set; } = true;
        public bool Relevance { get; set; } = true;
        public bool Fuzzy { get; set; } = true;
        public bool Wildcard { get; set; } = true;
        /// <summary>Override FindToolboxOptions.FuzzyUseLanguageField / WildcardUseLanguageField (tuning; test sites only).</summary>
        public bool? FuzzyLanguageField { get; set; }
        public bool? WildcardLanguageField { get; set; }

        /// <summary>Bypass caches (tests).</summary>
        public bool NoCache { get; set; }
    }

    public class ProductHit
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Brand { get; set; }
        public string Color { get; set; }
        public double Price { get; set; }
        public double Score { get; set; }
    }

    public class ProductSearchResult
    {
        public ProductSearchRequest Request { get; set; }
        public int Total { get; set; }
        public List<ProductHit> Hits { get; set; } = new List<ProductHit>();
        /// <summary>facet name => value => count (category, brand, color, price).</summary>
        public Dictionary<string, Dictionary<string, int>> Facets { get; set; } = new Dictionary<string, Dictionary<string, int>>();
        /// <summary>The counts for the query without any facet selections ("of N" in the UI).</summary>
        public Dictionary<string, Dictionary<string, int>> FacetTotals { get; set; } = new Dictionary<string, Dictionary<string, int>>();
        public string ToolboxChain { get; set; }
        public string QueryJson { get; set; }
    }
}
