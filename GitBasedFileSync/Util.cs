namespace GitBasedFileSync;

public static class Util
{
    public static (bool IsValid, string ErrorMessage) ValidatePath(string path)
    {
        // 1. 检查空值
        if (string.IsNullOrEmpty(path))
            return (false, "路径不能为空");

        try
        {
            // 2. 检查路径格式是否合法
            if (Path.GetInvalidPathChars().Any(path.Contains))
                return (false, "路径包含非法字符");

            // 3. 检查是否为绝对路径
            if (!Path.IsPathRooted(path))
                return (false, "必须是绝对路径");

            // 4. 获取完整路径（规范化路径格式）
            var fullPath = Path.GetFullPath(path);

            // 5. 检查路径是否存在且是文件夹
            if (!Directory.Exists(fullPath))
                return (false, "文件夹不存在");

            // 6. 额外验证：确保不是文件路径
            return File.Exists(fullPath) ? (false, "路径不是文件夹") : (true, "路径有效");
        }
        catch (ArgumentException ex)
        {
            return (false, $"路径格式无效: {ex.Message}");
        }
        catch (PathTooLongException)
        {
            return (false, "路径过长（超过系统限制）");
        }
        catch (NotSupportedException)
        {
            return (false, "路径包含无效格式");
        }
    }
}