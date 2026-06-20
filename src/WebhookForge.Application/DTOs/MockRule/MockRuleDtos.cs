using System.ComponentModel.DataAnnotations;

namespace WebhookForge.Application.DTOs.MockRule;

/// <summary>Request body for POST /api/endpoints/{id}/rules.</summary>
public class CreateMockRuleDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Lower = higher priority. Gap of 10 between rules allows easy insertion.</summary>
    public int Priority { get; set; }

    /// <summary>HTTP method to match (GET, POST, etc.). Null matches any method.</summary>
    public string? MatchMethod { get; set; }

    /// <summary>Regex or exact path to match. Null matches any path.</summary>
    public string? MatchPath { get; set; }

    /// <summary>Regex applied to the request body. Null means no body constraint.</summary>
    public string? MatchBodyExpression { get; set; }

    [Range(100, 599)]
    public short ResponseStatus { get; set; } = 200;

    public string? ResponseBody { get; set; }

    /// <summary>Response headers to send. Serialized to JSON in the service layer.</summary>
    public Dictionary<string, string>? ResponseHeaders { get; set; }

    /// <summary>Artificial delay in milliseconds before the response is sent.</summary>
    public int DelayMs { get; set; }
}

/// <summary>Request body for PUT /api/rules/{id}. Inherits all create fields and adds IsActive.</summary>
public class UpdateMockRuleDto : CreateMockRuleDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>Mock rule representation returned by list and detail endpoints.</summary>
public class MockRuleDto
{
    public Guid    Id                  { get; set; }
    public Guid    EndpointId          { get; set; }
    public string  Name                { get; set; } = string.Empty;
    public int     Priority            { get; set; }
    public string? MatchMethod         { get; set; }
    public string? MatchPath           { get; set; }
    public string? MatchBodyExpression { get; set; }
    public short   ResponseStatus      { get; set; }
    public string? ResponseBody        { get; set; }

    /// <summary>Deserialized response headers.</summary>
    public Dictionary<string, string>? ResponseHeaders { get; set; }

    public int     DelayMs   { get; set; }
    public bool    IsActive  { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request body for PUT /api/endpoints/{id}/rules/reorder.
/// The supplied order determines new priority values (first ID = priority 10, second = 20, etc.).
/// </summary>
public class ReorderRulesDto
{
    /// <summary>Rule IDs in the desired evaluation order, highest-priority first.</summary>
    public List<Guid> RuleIds { get; set; } = new();
}
