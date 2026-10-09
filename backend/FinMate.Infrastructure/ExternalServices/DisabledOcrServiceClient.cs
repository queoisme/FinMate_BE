using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>Khi OCR_SERVICE_URL chưa đặt: <see cref="IsConfigured"/> false nên handler quay về <c>POST /api/v1/ocr</c> của AI Service.</summary>
public class DisabledOcrServiceClient : IOcrServiceClient
{
    public bool IsConfigured => false;

    public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException("OCR service chưa được cấu hình (OCR_SERVICE_URL).");
}
