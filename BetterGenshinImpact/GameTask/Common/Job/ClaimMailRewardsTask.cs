using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取邮件奖励
/// </summary>
public class ClaimMailRewardsTask
{
    private readonly ReturnMainUiTask _returnMainUiTask = new();

    public async Task Start(CancellationToken ct)
    {
        try
        {
            var result = await DoOnce(ct);
            EmitMailEvent(result);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "领取邮件奖励异常");
            Logger.LogError("领取邮件奖励异常: {Msg}", e.Message);
            EmitMailEvent(new MailClaimResult("failed", false, false, false));
        }
    }

    private async Task<MailClaimResult> DoOnce(CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await Delay(200, ct);

        // 打开派蒙菜单
        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenPaimonMenu); // ESC 

        // 循环等待派蒙菜单动画完全展开，并检测邮件图标（带红点有奖邮件）
        bool mailIconFound = await WaitForMailRewardIconAsync(ct);
        bool claimAllClicked = false;
        bool claimAllGone = false;
        bool mailWindowOpened = false;

        if (mailIconFound)
        {
            // 点击邮件图标，直到邮件界面成功打开（右上角关闭按钮或全部领取按钮出现）
            mailWindowOpened = await OpenMailWindowAsync(ct);
            if (mailWindowOpened)
            {
                // 等待全部领取按钮出现并点击（邮件列表和附件可能需要异步加载）
                claimAllClicked = await TryClickCollectButtonAsync(ct);
                if (claimAllClicked)
                {
                    Logger.LogInformation("邮件：{Text}", "全部领取");
                    await Delay(700, ct);
                    claimAllGone = await WaitForClaimButtonToDisappear(ct);
                    if (!claimAllGone)
                    {
                        throw new Exception("领取邮件后仍检测到全部领取按钮");
                    }

                    // 领取后会有“获得物品”弹窗，按 ESC 关闭弹窗
                    await Delay(500, ct);
                    TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE);
                    await Delay(500, ct);
                }
                else
                {
                    Logger.LogInformation("邮件：{Text}", "没有可领取邮件奖励");
                    // 没有全部领取按钮，按 ESC 退出邮件窗口
                    TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE);
                    await Delay(500, ct);
                }
            }
            else
            {
                Logger.LogWarning("未能成功打开邮件界面");
            }
        }
        else
        {
            Logger.LogInformation("邮件：{Text}", "没有邮件奖励");
        }

        // 关闭任何残留窗口，安全返回主界面
        await _returnMainUiTask.Start(ct);

        string finalStatus;
        if (claimAllClicked)
        {
            finalStatus = "success";
        }
        else if (!mailIconFound)
        {
            finalStatus = "skipped";
        }
        else if (mailWindowOpened)
        {
            // 界面打开但无附件可领
            finalStatus = "skipped";
        }
        else
        {
            // 检测到红点图标但多次尝试均未能打开界面
            finalStatus = "failed";
        }

        return new MailClaimResult(
            finalStatus,
            mailIconFound,
            claimAllClicked,
            claimAllGone);
    }

    private static async Task<bool> WaitForMailRewardIconAsync(CancellationToken ct)
    {
        // 派蒙菜单淡入约需 1.2~1.8 秒，等待最多 3.5 秒
        for (int i = 0; i < 14; i++)
        {
            await Delay(250, ct);
            using var ra = CaptureToRectArea();
            var icon = ra.Find(ElementRecognition.Get("EscMailReward", ra));
            if (icon.IsExist())
            {
                return true;
            }

            // 约 1.5 秒时如果依然在大世界界面（ESC 丢失未打开菜单），尝试补按一次 ESC
            if (i == 6 && Bv.IsInMainUi(ra))
            {
                TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenPaimonMenu);
            }
        }

        return false;
    }

    private static async Task<bool> OpenMailWindowAsync(CancellationToken ct)
    {
        // 点击邮件图标，并等待邮件窗口打开（最多重试 3 次）
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using (var ra = CaptureToRectArea())
            {
                var icon = ra.Find(ElementRecognition.Get("EscMailReward", ra));
                if (icon.IsExist())
                {
                    icon.Click();
                }
            }

            // 等待邮件界面打开（检测右上角关闭按钮 PageCloseWhite 或 全部领取 Collect）
            for (int i = 0; i < 6; i++)
            {
                await Delay(300, ct);
                using var checkArea = CaptureToRectArea();
                if (checkArea.Find(ElementRecognition.Get("PageCloseWhite", checkArea)).IsExist() ||
                    checkArea.Find(ElementRecognition.Get("Collect", checkArea)).IsExist())
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> TryClickCollectButtonAsync(CancellationToken ct)
    {
        // 邮件附件可能需要异步加载，等待最多 2.5 秒
        for (int i = 0; i < 8; i++)
        {
            using var claimArea = CaptureToRectArea();
            var claimAll = claimArea.Find(ElementRecognition.Get("Collect", claimArea));
            if (claimAll.IsExist())
            {
                claimAll.Click();
                return true;
            }
            await Delay(300, ct);
        }

        return false;
    }

    private static async Task<bool> WaitForClaimButtonToDisappear(CancellationToken ct)
    {
        return await NewRetry.WaitForAction(() =>
        {
            using var capture = CaptureToRectArea();
            return !capture.Find(ElementRecognition.Get("Collect", capture)).IsExist();
        }, ct, 12, 300);
    }

    private static void EmitMailEvent(MailClaimResult result)
    {
        var payload = JsonConvert.SerializeObject(new
        {
            task = "领取邮件",
            result.Status,
            evidenceVerified = true,
            result.MailIconFound,
            result.ClaimAllClicked,
            result.ClaimAllGone,
        });
        Logger.LogInformation("[AUTO_GAME_EVENT] " + payload);
    }

    private sealed record MailClaimResult(
        string Status,
        bool MailIconFound,
        bool ClaimAllClicked,
        bool ClaimAllGone);
}
