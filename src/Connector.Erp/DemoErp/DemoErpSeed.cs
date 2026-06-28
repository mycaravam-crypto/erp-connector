namespace Connector.Erp.DemoErp;

/// <summary>
/// Befüllt die Demo-ERP-Datenbank mit einem realistischen Testszenario.
/// </summary>
/// <remarks>
/// Szenario: Ein Industrie-Rack mit fünf eingebauten Komponenten.
/// Fünf davon haben einen aktiven Wartungsplan (→ im Export-Scope).
/// Zwei sind ausgeschlossen:
///   - sc-psu-0002: kein Wartungsplan
///   - sc-rack-0002: Wartungsplan vorhanden, aber Inactive
///
/// Jeder SystemConfiguration-Datensatz enthält TechnicianName (personenbezogen)
/// und StorageLocation (Open Point #4) — beide werden durch DataMinimizer entfernt.
/// </remarks>
public static class DemoErpSeed
{
    // Stabile IDs — werden in Tests verwendet, um erwartete Datensätze zu prüfen.
    public static class Ids
    {
        // Masterdata
        public const string MdRack = "md-rack-001";
        public const string MdBlade = "md-blade-001";
        public const string MdPsu = "md-psu-001";
        public const string MdSwitch = "md-switch-001";

        // SystemConfiguration
        public const string ScRack1 = "sc-rack-0001";
        public const string ScBlade1 = "sc-blade-0001";
        public const string ScBlade2 = "sc-blade-0002";
        public const string ScPsu1 = "sc-psu-0001";
        public const string ScPsu2 = "sc-psu-0002";
        public const string ScSwitch1 = "sc-sw-0001";
        public const string ScRack2 = "sc-rack-0002";

        // Seriennummern (für Test-Assertions)
        public const string SnRack1 = "SN-RACK-0001";
        public const string SnBlade1 = "SN-BLD-0001";
        public const string SnBlade2 = "SN-BLD-0002";
        public const string SnPsu1 = "SN-PSU-0001";
        public const string SnPsu2 = "SN-PSU-0002";
        public const string SnSwitch1 = "SN-SW-0001";
        public const string SnRack2 = "SN-RACK-0002";

        public static readonly IReadOnlySet<string> InScopeSerials = new HashSet<string>
        {
            SnRack1,
            SnBlade1,
            SnBlade2,
            SnPsu1,
            SnSwitch1,
        };
    }

    public static void Seed(DemoErpDbContext db)
    {
        if (db.Masterdata.Any())
            return;

        var models = new[]
        {
            new ErpMasterdata
            {
                Id = Ids.MdRack,
                ArticleName = "Industrial Rack System",
                PartNumber = "P-RACK-42U",
                Manufacturer = "TechCorp GmbH",
            },
            new ErpMasterdata
            {
                Id = Ids.MdBlade,
                ArticleName = "Compute Module MK2",
                PartNumber = "P-BLADE-CM2",
                Manufacturer = "TechCorp GmbH",
            },
            new ErpMasterdata
            {
                Id = Ids.MdPsu,
                ArticleName = "Power Supply 2400W",
                PartNumber = "P-PSU-2400",
                Manufacturer = "PowerTech AG",
            },
            new ErpMasterdata
            {
                Id = Ids.MdSwitch,
                ArticleName = "Managed Switch 24P",
                PartNumber = "P-SW-24P",
                Manufacturer = "NetGear Industrial",
            },
        };
        db.Masterdata.AddRange(models);

        var configs = new[]
        {
            // Rack 1 — root CI, hat einen aktiven Wartungsplan → im Scope
            new ErpSystemConfiguration
            {
                Id = Ids.ScRack1,
                Serial = Ids.SnRack1,
                ArticleId = Ids.MdRack,
                Status = "Active",
                CommissionDate = new DateOnly(2023, 3, 1),
                TechnicianName = "Klaus Bauer",
                StorageLocation = "Halle A, Reihe 3",
            },
            // Blade 1 — Kind von Rack 1, aktiver Wartungsplan → im Scope
            new ErpSystemConfiguration
            {
                Id = Ids.ScBlade1,
                Serial = Ids.SnBlade1,
                ArticleId = Ids.MdBlade,
                Status = "Active",
                CommissionDate = new DateOnly(2023, 3, 15),
                TechnicianName = "Klaus Bauer",
                StorageLocation = "Halle A, Reihe 3, Slot 1",
            },
            // Blade 2 — in Reparatur, Wartungsplan aktiv → im Scope
            new ErpSystemConfiguration
            {
                Id = Ids.ScBlade2,
                Serial = Ids.SnBlade2,
                ArticleId = Ids.MdBlade,
                Status = "InRepair",
                CommissionDate = new DateOnly(2023, 3, 15),
                TechnicianName = "Anna Fischer",
                StorageLocation = "Reparaturwerkstatt B",
            },
            // PSU 1 — aktiver Wartungsplan → im Scope
            new ErpSystemConfiguration
            {
                Id = Ids.ScPsu1,
                Serial = Ids.SnPsu1,
                ArticleId = Ids.MdPsu,
                Status = "Active",
                CommissionDate = new DateOnly(2023, 2, 28),
                TechnicianName = "Klaus Bauer",
                StorageLocation = "Halle A, Reihe 3, PSU-Bay 1",
            },
            // PSU 2 — KEIN Wartungsplan → ausgeschlossen
            new ErpSystemConfiguration
            {
                Id = Ids.ScPsu2,
                Serial = Ids.SnPsu2,
                ArticleId = Ids.MdPsu,
                Status = "Active",
                CommissionDate = new DateOnly(2023, 2, 28),
                TechnicianName = "Anna Fischer",
                StorageLocation = "Halle A, Reihe 3, PSU-Bay 2",
            },
            // Switch 1 — aktiver Wartungsplan → im Scope
            new ErpSystemConfiguration
            {
                Id = Ids.ScSwitch1,
                Serial = Ids.SnSwitch1,
                ArticleId = Ids.MdSwitch,
                Status = "Active",
                CommissionDate = new DateOnly(2023, 4, 1),
                TechnicianName = "Klaus Bauer",
                StorageLocation = "Halle A, Reihe 3, Switch-Bay",
            },
            // Rack 2 — INAKTIVER Wartungsplan → ausgeschlossen
            new ErpSystemConfiguration
            {
                Id = Ids.ScRack2,
                Serial = Ids.SnRack2,
                ArticleId = Ids.MdRack,
                Status = "Decommissioned",
                CommissionDate = new DateOnly(2020, 1, 15),
                TechnicianName = "Klaus Bauer",
                StorageLocation = "Lager C",
            },
        };
        db.SystemConfigurations.AddRange(configs);

        // BOM-Struktur: Rack 1 ist Elternteil von blade-1, blade-2, psu-1, psu-2, switch-1
        var bom = new[]
        {
            new ErpArticleStructure
            {
                Id = "bom-rack1-blade1",
                ParentId = Ids.ScRack1,
                ChildId = Ids.ScBlade1,
            },
            new ErpArticleStructure
            {
                Id = "bom-rack1-blade2",
                ParentId = Ids.ScRack1,
                ChildId = Ids.ScBlade2,
            },
            new ErpArticleStructure
            {
                Id = "bom-rack1-psu1",
                ParentId = Ids.ScRack1,
                ChildId = Ids.ScPsu1,
            },
            new ErpArticleStructure
            {
                Id = "bom-rack1-psu2",
                ParentId = Ids.ScRack1,
                ChildId = Ids.ScPsu2,
            },
            new ErpArticleStructure
            {
                Id = "bom-rack1-sw1",
                ParentId = Ids.ScRack1,
                ChildId = Ids.ScSwitch1,
            },
        };
        db.ArticleStructures.AddRange(bom);

        // Wartungspläne — nur "Active" zählt als Scope-Kriterium
        var plans = new[]
        {
            new ErpMaintenancePlan
            {
                Id = "mp-rack1",
                SystemConfigurationId = Ids.ScRack1,
                Status = "Active",
                AllocationChartRef = "ALLOC-2023-V1",
            },
            new ErpMaintenancePlan
            {
                Id = "mp-blade1",
                SystemConfigurationId = Ids.ScBlade1,
                Status = "Active",
                AllocationChartRef = "ALLOC-2023-V1",
            },
            new ErpMaintenancePlan
            {
                Id = "mp-blade2",
                SystemConfigurationId = Ids.ScBlade2,
                Status = "Active",
                AllocationChartRef = "ALLOC-2023-V1",
            },
            new ErpMaintenancePlan
            {
                Id = "mp-psu1",
                SystemConfigurationId = Ids.ScPsu1,
                Status = "Active",
                AllocationChartRef = "ALLOC-2023-V1",
            },
            // ScPsu2 hat keinen Eintrag → ausgeschlossen
            new ErpMaintenancePlan
            {
                Id = "mp-switch1",
                SystemConfigurationId = Ids.ScSwitch1,
                Status = "Active",
                AllocationChartRef = "ALLOC-2023-V1",
            },
            new ErpMaintenancePlan
            {
                Id = "mp-rack2",
                SystemConfigurationId = Ids.ScRack2,
                Status = "Inactive",
                AllocationChartRef = "ALLOC-2020-V1",
            },
        };
        db.MaintenancePlans.AddRange(plans);

        db.SaveChanges();
    }
}
