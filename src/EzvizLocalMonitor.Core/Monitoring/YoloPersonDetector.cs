using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class YoloPersonDetector : IDisposable
{
    private const int InputSize = 640;
    private readonly InferenceSession _session;
    private readonly string _inputName;

    public YoloPersonDetector(string? modelPath = null)
    {
        modelPath ??= Path.Combine(AppContext.BaseDirectory, "Models", "yolov8n.onnx");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Không tìm thấy mô hình nhận diện. Hãy chạy bộ cài đầy đủ hoặc đặt yolov8n.onnx vào thư mục Models.", modelPath);

        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.Single();
    }

    public IReadOnlyList<PersonDetection> Detect(Mat frame, double confidenceThreshold)
    {
        if (frame.Empty()) return Array.Empty<PersonDetection>();

        var scale = Math.Min(InputSize / (double)frame.Width, InputSize / (double)frame.Height);
        var resizedWidth = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(frame.Height * scale));
        var padX = (InputSize - resizedWidth) / 2;
        var padY = (InputSize - resizedHeight) / 2;

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new Size(resizedWidth, resizedHeight));
        using var letterboxed = new Mat(new Size(InputSize, InputSize), MatType.CV_8UC3, new Scalar(114, 114, 114));
        using (var region = new Mat(letterboxed, new Rect(padX, padY, resizedWidth, resizedHeight))) resized.CopyTo(region);

        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var pixel = letterboxed.At<Vec3b>(y, x); // OpenCV is BGR.
                tensor[0, 0, y, x] = pixel.Item2 / 255f;
                tensor[0, 1, y, x] = pixel.Item1 / 255f;
                tensor[0, 2, y, x] = pixel.Item0 / 255f;
            }
        }

        var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
        using var results = _session.Run(new[] { input });
        var output = results.First().AsTensor<float>(); // Standard YOLOv8 detection export: [1, 84, 8400].

        var candidates = new List<PersonDetection>();
        var proposalCount = output.Dimensions[^1];
        for (var i = 0; i < proposalCount; i++)
        {
            var confidence = output[0, 4, i]; // COCO class 0 = person.
            if (confidence < confidenceThreshold) continue;

            var centerX = output[0, 0, i];
            var centerY = output[0, 1, i];
            var width = output[0, 2, i];
            var height = output[0, 3, i];

            var left = (int)Math.Round((centerX - width / 2 - padX) / scale);
            var top = (int)Math.Round((centerY - height / 2 - padY) / scale);
            var right = (int)Math.Round((centerX + width / 2 - padX) / scale);
            var bottom = (int)Math.Round((centerY + height / 2 - padY) / scale);

            left = Math.Clamp(left, 0, frame.Width - 1);
            top = Math.Clamp(top, 0, frame.Height - 1);
            right = Math.Clamp(right, 0, frame.Width - 1);
            bottom = Math.Clamp(bottom, 0, frame.Height - 1);
            if (right <= left || bottom <= top) continue;

            candidates.Add(new PersonDetection(confidence, left, top, right, bottom));
        }

        return NonMaximumSuppression(candidates, 0.45);
    }

    private static IReadOnlyList<PersonDetection> NonMaximumSuppression(IEnumerable<PersonDetection> candidates, double iouThreshold)
    {
        var retained = new List<PersonDetection>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Confidence))
        {
            if (retained.All(accepted => IoU(candidate, accepted) < iouThreshold)) retained.Add(candidate);
        }
        return retained;
    }

    private static double IoU(PersonDetection a, PersonDetection b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = a.Width * a.Height + b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / (double)union;
    }

    public void Dispose() => _session.Dispose();
}
