namespace FinMate.Domain.Enums;

// Khớp field "pipeline_result" trong contract AI Service (ARCHITECTURE.md §3.3).
public enum PipelineResult
{
    Financial,
    NonFinancial,
    Uncertain,
    ExtractionFailed,
    Error,
}
