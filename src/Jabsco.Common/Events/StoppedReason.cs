namespace Jabsco.Common.Events;

// Defined here (Common) so AgentStats can reference it without a circular dependency.
public enum StoppedReason { ModelDone, StepBudget, TimeBudget, Error, UserCancel }
