namespace EzvizLocalMonitor.Services;

public static class DeliveryStatusPolicy
{
    public static bool IsPending(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return true;
        status = status.Trim();
        if (status.Equals("Không có kênh nào được bật", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Không có cấu hình cảnh báo", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Không gửi: AI không thấy chuyển động/người", StringComparison.OrdinalIgnoreCase)) return false;

        // Queue interruption/overflow and coordinator shutdown never finish delivery.
        if (status.StartsWith("Chưa gửi", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Cảnh báo: hàng đợi", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Cảnh báo: worker lỗi", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Cảnh báo: thao tác đã hủy", StringComparison.OrdinalIgnoreCase)) return true;

        var operations = status.Split(new[] { " | ", " + " }, StringSplitOptions.TrimEntries);
        return operations.Any(operation => !IsCompletedOperation(operation));
    }

    private static bool IsCompletedOperation(string operation)
    {
        var separator = operation.IndexOf(':');
        var result = separator >= 0 ? operation[(separator + 1)..].Trim() : operation.Trim();
        if (result.Equals("đã gửi", StringComparison.OrdinalIgnoreCase) ||
            result.Equals("đã gửi (idempotent)", StringComparison.OrdinalIgnoreCase)) return true;
        // Exhausted retries are terminal failures, unlike interrupted queue work.
        if (result.StartsWith("lỗi ", StringComparison.OrdinalIgnoreCase) ||
            result.StartsWith("lần gửi 3 lỗi (", StringComparison.OrdinalIgnoreCase) ||
            result.Equals("relay HTTPS thất bại", StringComparison.OrdinalIgnoreCase)) return true;
        // Intentional text-only Zalo delivery is a completed operation.
        return operation.StartsWith("Zalo ảnh: chưa gửi; API yêu cầu URL HTTPS công khai", StringComparison.OrdinalIgnoreCase);
    }
}
