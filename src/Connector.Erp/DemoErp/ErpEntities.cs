namespace Connector.Erp.DemoErp;

/// <summary>
/// Modell-Stammdatensatz — entspricht der ERP-Tabelle "masterdata".
/// Repräsentiert einen Artikeltyp (nicht eine physische Einheit).
/// </summary>
public sealed class ErpMasterdata
{
    public string Id { get; set; } = string.Empty;
    public string? ArticleName { get; set; }
    public string? PartNumber { get; set; }
    public string? Manufacturer { get; set; }

    public ICollection<ErpSystemConfiguration> Instances { get; set; } = [];
}

/// <summary>
/// Installierte CI-Instanz — entspricht der ERP-Tabelle "systemconfiguration".
/// Trägt die physische Seriennummer, den Installationsstatus und wartungsrelevante Felder.
/// Enthält auch Felder, die durch <c>DataMinimizer</c> ausgeschlossen werden (TechnicianName, StorageLocation).
/// </summary>
public sealed class ErpSystemConfiguration
{
    public string Id { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? ArticleId { get; set; }
    public string? Status { get; set; }
    public DateOnly? CommissionDate { get; set; }

    // Personenbezogenes Feld — wird durch DataMinimizer ausgeschlossen (DSGVO Art. 5 Abs. 1 lit. c).
    public string? TechnicianName { get; set; }

    // Open Point #4: Aufnahme in Scope noch nicht bestätigt.
    public string? StorageLocation { get; set; }

    public ErpMasterdata? Article { get; set; }
    public ICollection<ErpMaintenancePlan> MaintenancePlans { get; set; } = [];

    /// <summary>BOM-Links, bei denen diese Instanz das ÜBERGEORDNETE Element ist.</summary>
    public ICollection<ErpArticleStructure> ChildLinks { get; set; } = [];

    /// <summary>BOM-Links, bei denen diese Instanz das UNTERGEORDNETE Element ist.</summary>
    public ICollection<ErpArticleStructure> ParentLinks { get; set; } = [];
}

/// <summary>
/// BOM-Eltern-Kind-Beziehung — entspricht der ERP-Tabelle "articlestructure".
/// Enthält immer genau einen Eltern- und einen Kind-Datensatz.
/// </summary>
public sealed class ErpArticleStructure
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? ChildId { get; set; }

    public ErpSystemConfiguration? Parent { get; set; }
    public ErpSystemConfiguration? Child { get; set; }
}

/// <summary>
/// Wartungsplan — entspricht der ERP-Tabelle "maintenance_plan".
/// Ein aktiver Plan ist Voraussetzung für die Aufnahme in den Export (Scope-Filter).
/// Der Plan stammt aus dem Wartungszuordnungsplan des Herstellers, der ins ERP importiert wird.
/// </summary>
public sealed class ErpMaintenancePlan
{
    public string Id { get; set; } = string.Empty;
    public string? SystemConfigurationId { get; set; }

    /// <summary>"Active" = im Scope; "Inactive" = ausgeschlossen.</summary>
    public string Status { get; set; } = string.Empty;

    public string? AllocationChartRef { get; set; }

    public ErpSystemConfiguration? SystemConfiguration { get; set; }
}
