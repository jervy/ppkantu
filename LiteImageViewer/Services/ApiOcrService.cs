using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LiteImageViewer.Config;
using LiteImageViewer.Models;

namespace LiteImageViewer.Services;

/// <summary>
/// 第三方 OCR API 服务
/// 支持自定义 HTTP API，兼容百度OCR、腾讯OCR、阿里OCR 等
/// 需要通过配置文件或环境变量提供 API Key
/// </summary>
public class ApiOcrService : IOcrService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;

    public string ServiceName => "API OCR";

    public ApiOcrService(AppSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.OcrApiEndpoint))
        {
            return new OcrResult
            {
                IsSuccess = false,
                ErrorMessage = "未配置 OCR API 地址。请在配置文件中设置 OcrApiEndpoint。"
            };
        }

        if (string.IsNullOrEmpty(_settings.OcrApiKey))
        {
            return new OcrResult
            {
                IsSuccess = false,
                ErrorMessage = "未配置 OCR API 密钥。请在配置文件或环境变量 OCR_API_KEY 中设置。"
            };
        }

        try
        {
            // 读取图片并转为 Base64
            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var base64Image = Convert.ToBase64String(imageBytes);

            // 构建请求体（通用 JSON 格式，可按具体 API 调整）
            var requestBody = new
            {
                image = base64Image,
                // TODO: 根据不同 OCR 服务商添加特定参数
                // 例如百度OCR需要image字段，腾讯OCR需要ImageBase64字段
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _settings.OcrApiEndpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_settings.OcrApiKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            // TODO: 根据具体 OCR API 的响应格式解析结果
            // 以下为通用解析示例
            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var ocrResult = new OcrResult { IsSuccess = true };

            // 尝试解析常见的响应格式
            if (result.TryGetProperty("text", out var textProp))
            {
                ocrResult.Text = textProp.GetString() ?? string.Empty;
            }
            else if (result.TryGetProperty("result", out var resultProp))
            {
                ocrResult.Text = resultProp.GetString() ?? string.Empty;
            }
            else if (result.TryGetProperty("data", out var dataProp))
            {
                ocrResult.Text = dataProp.GetString() ?? string.Empty;
            }
            else
            {
                ocrResult.Text = responseJson;
            }

            return ocrResult;
        }
        catch (OperationCanceledException)
        {
            return new OcrResult
            {
                IsSuccess = false,
                ErrorMessage = "OCR 识别已取消。"
            };
        }
        catch (HttpRequestException ex)
        {
            return new OcrResult
            {
                IsSuccess = false,
                ErrorMessage = $"OCR API 请求失败: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new OcrResult
            {
                IsSuccess = false,
                ErrorMessage = $"OCR 识别失败: {ex.Message}"
            };
        }
    }
}
