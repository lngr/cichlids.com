namespace Cichlids.Domain.Enums;

/// <summary>
/// What a decommissioning of a tank pauses: AllCare pauses every kind of care (nothing is due, no
/// reminder falls, the care heatmap marks the time), WaterChange pauses only the water change and its
/// streak while measurements stay due with their reminders.
/// </summary>
public enum DecommissionScope { AllCare, WaterChange }
