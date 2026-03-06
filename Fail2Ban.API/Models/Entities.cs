using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fail2Ban.API.Models;

public class BanRecord
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string IpAddress { get; set; } = string.Empty;
    
    public DateTime BannedAt { get; set; }
    
    public DateTime? ExpiresAt { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public bool IsAbuseReported { get; set; } = false;
    
    public int FailedAttemptCount { get; set; }
    
    public string FirewallRuleName { get; set; } = string.Empty;
}

public class LogPointer
{
    [Key]
    public string FilePath { get; set; } = string.Empty;
    
    public long LastReadPosition { get; set; }
    
    public DateTime LastReadAt { get; set; }
}

public class WhitelistedIp
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string IpAddress { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public DateTime AddedAt { get; set; }
}

public class FailedAttempt
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string IpAddress { get; set; } = string.Empty;
    
    public int Count { get; set; }
    
    public DateTime FirstAttemptAt { get; set; }
    
    public DateTime LastAttemptAt { get; set; }
    
    public string Source { get; set; } = string.Empty;
}
