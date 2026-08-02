using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;

namespace OhMyBot.Core.Commanding.Presentation;

/// <summary>
/// 交互面板的机制层：封装回调 payload 生成、按钮分栏、翻页行。
///
/// 这里不含任何面向用户的文案——按钮文字一律由调用方（插件）传入，以保持
/// 「Core 负责权限判断和字段裁剪，不在 Core 拼平台最终文案」的边界。
/// 每次构建面板新建一个实例（绑定当前 <see cref="CommandContext"/>），开销只有一次对象分配。
/// </summary>
public sealed class PanelBuilder(
    CallbackActionStore callbackStore,
    CommandContext context,
    string? ownerPluginId = null)
{
    /// <summary>生成一个带一次性回调 payload 的按钮。</summary>
    public async Task<ResponseButton> ButtonAsync(
        string actionType,
        string text,
        object data,
        CancellationToken cancellationToken = default)
    {
        return new ResponseButton
        {
            Text = text,
            Payload = await callbackStore.PutAsync(
                actionType,
                context.Identity.CoreUserId,
                context.Request.ChatId,
                context.Request.UserId,
                data,
                ownerPluginId: ownerPluginId,
                cancellationToken: cancellationToken)
        };
    }

    /// <summary>追加只含一个按钮的一行。</summary>
    public async Task<CommandResponse> AddRowAsync(
        CommandResponse response,
        string actionType,
        string text,
        object data,
        CancellationToken cancellationToken = default)
    {
        return response.AddButtonRow(Row(await ButtonAsync(actionType, text, data, cancellationToken)));
    }

    /// <summary>把若干条目按固定列数排成按钮网格；不足一行的余量单独成行。</summary>
    public async Task<CommandResponse> AddGridAsync<TItem>(
        CommandResponse response,
        IEnumerable<TItem> items,
        int columns,
        string actionType,
        Func<TItem, string> textSelector,
        Func<TItem, object> dataSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        var row = new ResponseButtonRow();
        foreach (var item in items)
        {
            row.Buttons.Add(await ButtonAsync(actionType, textSelector(item), dataSelector(item), cancellationToken));
            if (row.Buttons.Count == columns)
            {
                response.AddButtonRow(row);
                row = new ResponseButtonRow();
            }
        }

        if (row.Buttons.Count > 0)
        {
            response.AddButtonRow(row);
        }

        return response;
    }

    /// <summary>
    /// 翻页行：总页数 ≤ 1 时不追加任何行，首页省略「上一页」、末页省略「下一页」。
    /// 用 <see cref="Func{T,TResult}"/> 而非泛型参数，因为各插件的翻页回调 record 类型互不相同。
    /// </summary>
    public async Task<CommandResponse> AddPagerAsync(
        CommandResponse response,
        string actionType,
        int page,
        int totalPages,
        Func<int, object> pageDataFactory,
        string previousText,
        string nextText,
        CancellationToken cancellationToken = default)
    {
        if (totalPages <= 1)
        {
            return response;
        }

        var row = new ResponseButtonRow();
        if (page > 0)
        {
            row.Buttons.Add(await ButtonAsync(actionType, previousText, pageDataFactory(page - 1), cancellationToken));
        }

        if (page + 1 < totalPages)
        {
            row.Buttons.Add(await ButtonAsync(actionType, nextText, pageDataFactory(page + 1), cancellationToken));
        }

        return row.Buttons.Count > 0 ? response.AddButtonRow(row) : response;
    }

    /// <summary>把若干按钮组成一行（不追加到响应）。</summary>
    public static ResponseButtonRow Row(params ResponseButton[] buttons)
    {
        var row = new ResponseButtonRow();
        row.Buttons.AddRange(buttons);
        return row;
    }
}
