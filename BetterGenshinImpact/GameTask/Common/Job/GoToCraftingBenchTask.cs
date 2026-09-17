using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.Localization;
using System.Globalization;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;
using OpenCvSharp;


namespace BetterGenshinImpact.GameTask.Common.Job;

public class GoToCraftingBenchTask
{
    private static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");
    private const int ResinConsumedPerCraft = 60;
    
    public string Name => "前往合成台";

    private readonly int _retryTimes = 2;
    private bool _craftActionCommitted;

    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();
    
    private  OneDragonFlowConfig? SelectedConfig;
    private ObservableCollection<OneDragonFlowConfig> ConfigList = [];
    
    private readonly string craftLocalizedString;

    public GoToCraftingBenchTask()
    {
        IStringLocalizer<GoToCraftingBenchTask> stringLocalizer = App.GetService<IStringLocalizer<GoToCraftingBenchTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.craftLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "合成");
    }
    
    public async Task GoCraftResin(string country, CancellationToken ct)
    {
        _craftActionCommitted = false;
        Logger.LogInformation("→ {Name} 开始", Name);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                await GoCraftResinOnce(country, ct);
                break;
            }
            catch (Exception e)
            {
                Logger.LogError("前往合成台领取奖励执行异常：" + e.Message);
                if (_craftActionCommitted || e is CraftingOutcomeUnknownException)
                {
                    throw;
                }
                if (i == _retryTimes - 1)
                {
                    // 通知失败
                    throw;
                }
                else
                {
                    await Delay(1000, ct);
                    Logger.LogInformation("重试前往合成台领取奖励");
                }
            }
        }

        Logger.LogInformation("→ {Name} 结束", Name);
    }

    public async Task GoCraftResinOnce(string country, CancellationToken ct)
    {
         // 1. 走到合成台并交互
        await GoToCraftingBenchOnce(country, ct);

        // 判断浓缩树脂是否存在
        // TODO 满的情况是怎么样子的
        using var ra = CaptureToRectArea();
        var resin = ra.Find(ElementRecognition.Get("CraftCondensedResin", ra));
        
        if (resin.IsExist())
        {
            InitConfigList();
            if (SelectedConfig?.AutoCraftAllCondensedResin == true || SelectedConfig?.MinResinToKeep > 0)
            {
                await CraftCondensedResinWithVerification(ct);
            }
            else
            {
                if (!Bv.ClickWhiteConfirmButton(ra))
                {
                    throw new Exception("未找到合成确认按钮");
                }
                _craftActionCommitted = true;
                await Delay(300, ct);
                using var confirmCapture = CaptureToRectArea();
                if (!Bv.ClickBlackConfirmButton(confirmCapture))
                {
                    throw new Exception("未找到合成结果确认按钮");
                }
                Logger.LogInformation("已点击合成{Text}，但未启用库存验证", "浓缩树脂");
            }
            await Delay(1300, ct);
            // 直接ESC退出即可
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        }
        else
        {
            Logger.LogInformation("无需合成浓缩树脂");
        }

        await new ReturnMainUiTask().Start(ct);
    }

    private async Task CraftCondensedResinWithVerification(CancellationToken ct)
    {
        const int maxCondensedResin = 5;
        var initial = await ReadCraftingResinCounts(ct);
        int minResinToKeep = Math.Max(0, SelectedConfig?.MinResinToKeep ?? 0);
        Logger.LogInformation(
            "浓缩树脂合成前核验：原粹树脂={OriginalResin}，浓缩树脂={CondensedResin}，保留={MinResinToKeep}",
            initial.OriginalResin, initial.CondensedResin, minResinToKeep);

        var safeUpperBound = ComputeSafeBatchCraftQuantity(
            initial.OriginalResin, initial.CondensedResin, minResinToKeep, maxCondensedResin);
        if (safeUpperBound == 0)
        {
            EmitCraftingEvent("skipped", initial, initial, 0, minResinToKeep);
            Logger.LogInformation("无需合成浓缩树脂，库存已核验");
            return;
        }

        // 游戏进入配方时通常已经选中可合成最大数量。先反复点击增加键抵达界面上限，
        // 再用 OCR 读取实际可合成量；整个批次只提交一次，减少重复确认和未知结果窗口。
        using (var maximizeCapture = CaptureToRectArea())
        {
            for (int i = 0; i < maxCondensedResin; i++)
            {
                _ = Bv.ClickAddButton(maximizeCapture);
                await Delay(120, ct);
            }
        }
        await Delay(250, ct);
        var displayedMaximum = await ReadStableCraftQuantity(ct);
        var targetQuantity = ComputeSafeBatchCraftQuantity(
            initial.OriginalResin, initial.CondensedResin, minResinToKeep, displayedMaximum);
        if (targetQuantity <= 0)
        {
            throw new Exception(
                $"界面可合成数量为 {displayedMaximum}，但树脂保留与库存校验不允许提交");
        }

        if (targetQuantity < displayedMaximum)
        {
            using var reduceCapture = CaptureToRectArea();
            for (int i = targetQuantity; i < displayedMaximum; i++)
            {
                if (!Bv.ClickReduceButton(reduceCapture))
                {
                    throw new Exception("未找到合成数量减少按钮");
                }
                await Delay(150, ct);
            }
            await Delay(250, ct);
            var adjustedQuantity = await ReadStableCraftQuantity(ct);
            if (adjustedQuantity != targetQuantity)
            {
                throw new Exception(
                    $"合成数量调整后核验失败：计划={targetQuantity}，界面={adjustedQuantity}");
            }
        }

        Logger.LogInformation(
            "浓缩树脂批量合成数量已连续三帧核验：界面上限={DisplayedMaximum}，本次提交={TargetQuantity}",
            displayedMaximum,
            targetQuantity);
        using var submitCapture = CaptureToRectArea();
        if (!Bv.ClickWhiteConfirmButton(submitCapture))
        {
            throw new Exception("未找到合成确认按钮");
        }
        _craftActionCommitted = true;

        CraftingResinCounts after;
        bool resultDismissed = false;
        try
        {
            resultDismissed = await NewRetry.WaitForAction(() =>
            {
                using var confirmCapture = CaptureToRectArea();
                return Bv.ClickBlackConfirmButton(confirmCapture);
            }, ct, 15, 400);
            if (!resultDismissed)
            {
                Logger.LogWarning("未识别到合成结果确认按钮，继续通过只读库存变化核验结果");
            }

            await Delay(1000, ct);
            after = await ReadCraftingResinCountsAfterCommittedAction(ct);
        }
        catch (Exception e)
        {
            throw new CraftingOutcomeUnknownException(
                "已点击批量合成确认，但结果或后库存无法确认；禁止自动重试", e);
        }

        if (!resultDismissed
            && IsCraftingInventoryUnchanged(
                initial.OriginalResin,
                initial.CondensedResin,
                after.OriginalResin,
                after.CondensedResin))
        {
            // 结果确认按钮没有出现，且第一次库存读取没有任何变化时，
            // 再等待并进行一次独立稳定读取。只有两次都确认完全未变化，
            // 才能把本次提交判定为未生效并安全重试当前节点。
            await Delay(1200, ct);
            CraftingResinCounts settled;
            try
            {
                settled = await ReadCraftingResinCounts(ct);
            }
            catch (Exception e)
            {
                throw new CraftingOutcomeUnknownException(
                    "已点击批量合成确认，但二次库存核验失败；禁止自动重试", e);
            }

            if (IsCraftingInventoryUnchanged(
                    initial.OriginalResin,
                    initial.CondensedResin,
                    settled.OriginalResin,
                    settled.CondensedResin))
            {
                _craftActionCommitted = false;
                throw new CraftingActionNotCommittedException(
                    $"提交后库存连续两次未变化：原粹/浓缩仍为 {settled.OriginalResin}/{settled.CondensedResin}；将在本节点重试");
            }

            after = settled;
        }

        int expectedOriginal = initial.OriginalResin - targetQuantity * ResinConsumedPerCraft;
        int expectedCondensed = initial.CondensedResin + targetQuantity;
        if (after.OriginalResin != expectedOriginal || after.CondensedResin != expectedCondensed)
        {
            throw new CraftingOutcomeUnknownException(
                $"批量合成后库存与计划不符：期望原粹/浓缩={expectedOriginal}/{expectedCondensed}，实际={after.OriginalResin}/{after.CondensedResin}；禁止自动重试");
        }

        EmitCraftingEvent("success", initial, after, targetQuantity, minResinToKeep);
        Logger.LogInformation("浓缩树脂批次核验完成：一次提交合成 {Crafted} 个", targetQuantity);
    }
    private async Task<int> ReadStableCraftQuantity(CancellationToken ct)
    {
        string raw = string.Empty;
        int recognizedQuantity = -1;
        int previousQuantity = -1;
        int consecutiveMatches = 0;
        int stableQuantity = -1;
        bool verified = await NewRetry.WaitForAction(() =>
        {
            using var capture = CaptureToRectArea();
            double scale = TaskContext.Instance().SystemInfo.AssetScale;
            using var quantityArea = capture.DeriveCrop(new Rect(
                (int)Math.Round(1248 * scale),
                (int)Math.Round(615 * scale),
                (int)Math.Round(221 * scale),
                (int)Math.Round(34 * scale)));
            raw = StringUtils.ConvertFullWidthNumToHalfWidth(OcrFactory.Paddle.Ocr(quantityArea.SrcMat)).Trim();
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"\d+");
            recognizedQuantity = match.Success ? StringUtils.TryParseInt(match.Value, -1) : -1;
            if (recognizedQuantity is < 1 or > 5)
            {
                previousQuantity = -1;
                consecutiveMatches = 0;
                return false;
            }

            consecutiveMatches = recognizedQuantity == previousQuantity ? consecutiveMatches + 1 : 1;
            previousQuantity = recognizedQuantity;
            if (consecutiveMatches < 3)
            {
                return false;
            }

            stableQuantity = recognizedQuantity;
            return true;
        }, ct, 15, 200);
        if (!verified || stableQuantity < 1)
        {
            throw new Exception(
                $"合成数量未能在确认前稳定核验：OCR='{raw}'，数量={recognizedQuantity}");
        }

        return stableQuantity;
    }

    internal static int ComputeSafeBatchCraftQuantity(
        int originalResin, int condensedResin, int minResinToKeep, int displayedMaximum)
    {
        if (originalResin is < 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(originalResin));
        }
        if (condensedResin is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(condensedResin));
        }
        if (minResinToKeep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minResinToKeep));
        }
        if (displayedMaximum is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(displayedMaximum));
        }

        var byResin = Math.Max(0, originalResin - minResinToKeep) / ResinConsumedPerCraft;
        var byCapacity = 5 - condensedResin;
        return Math.Min(displayedMaximum, Math.Min(byResin, byCapacity));
    }

    internal static bool IsCraftingInventoryUnchanged(
        int originalBefore,
        int condensedBefore,
        int originalAfter,
        int condensedAfter)
    {
        return originalBefore == originalAfter && condensedBefore == condensedAfter;
    }
    private async Task<CraftingResinCounts> ReadCraftingResinCounts(CancellationToken ct)
    {
        int originalResin = -1;
        int condensedResin = -1;
        string originalRaw = string.Empty;
        string condensedRaw = string.Empty;
        CraftingResinCounts? previous = null;
        CraftingResinCounts? stable = null;
        int consecutiveMatches = 0;
        bool recognized = await NewRetry.WaitForAction(() =>
        {
            using var capture = CaptureToRectArea();
            bool originalOk = TryReadOriginalResinCount(capture, out originalResin, out originalRaw);
            bool condensedOk = TryReadCondensedResinCount(capture, out condensedResin, out condensedRaw);
            if (!originalOk || !condensedOk)
            {
                previous = null;
                consecutiveMatches = 0;
                return false;
            }

            var current = new CraftingResinCounts(originalResin, condensedResin);
            consecutiveMatches = current == previous ? consecutiveMatches + 1 : 1;
            previous = current;
            if (consecutiveMatches < 3)
            {
                return false;
            }

            stable = current;
            return true;
        }, ct, 15, 250);
        if (!recognized || stable == null)
        {
            throw new Exception(
                $"识别合成库存失败：原粹='{originalRaw}' ({originalResin})，浓缩='{condensedRaw}' ({condensedResin})");
        }

        return stable;
    }

    private async Task<CraftingResinCounts> ReadCraftingResinCountsAfterCommittedAction(CancellationToken ct)
    {
        try
        {
            return await ReadCraftingResinCounts(ct);
        }
        catch (Exception firstReadException)
        {
            Logger.LogWarning(firstReadException,
                "合成后无法在当前界面读取库存，返回主界面并重新进入合成台进行只读核验");
        }

        // 合成动作已经提交，此处只允许恢复界面并读取库存，绝不再次点击合成按钮。
        await new ReturnMainUiTask().Start(ct);
        await ReenterCraftingUiForInventoryVerification(ct);
        return await ReadCraftingResinCounts(ct);
    }

    private async Task ReenterCraftingUiForInventoryVerification(CancellationToken ct)
    {
        bool enteredCraftingTalk = await WaitForCraftingTalkUi(ct);
        if (!enteredCraftingTalk)
        {
            await TryPressCrafting(ct);
            enteredCraftingTalk = await WaitForCraftingTalkUi(ct);
        }

        if (!enteredCraftingTalk)
        {
            throw new Exception("合成后库存核验时无法重新进入合成台对话");
        }

        await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct,
            region => region.Find(ElementRecognition.Get("BtnWhiteConfirm", region)).IsExist());
        await Delay(800, ct);
    }

    private static bool TryReadOriginalResinCount(ImageRegion region, out int count, out string raw)
    {
        count = -1;
        raw = string.Empty;
        var icon = region.Find(ElementRecognition.Get("fragileResinCount", region));
        if (icon.IsEmpty())
        {
            return false;
        }

        using var countArea = region.DeriveCrop(icon.X, icon.Y + icon.Height, icon.Width, icon.Height);
        raw = OcrFactory.Paddle.OcrWithoutDetector(countArea.SrcMat).Trim();
        return TryParseCraftingOriginalResin(raw, out count);
    }

    internal static bool TryParseCraftingOriginalResin(string raw, out int count)
    {
        count = -1;
        var normalized = StringUtils.ConvertFullWidthNumToHalfWidth(raw)
            .Replace('／', '/');
        // 合成界面显示“当前库存 / 单次所需”，当前版本单次消耗 60。
        // 兼容旧截图路径中出现的库存上限 160/200，以及斜杠被 OCR 成 1/7 的情况。
        var match = System.Text.RegularExpressions.Regex.Match(
            normalized,
            @"^\s*(?<current>\d{1,3})\s*[/17]\s*(?:60|160|200)\s*$");
        if (!match.Success)
        {
            return false;
        }

        count = StringUtils.TryParseInt(match.Groups["current"].Value, -1);
        if (count is < 0 or > 200)
        {
            count = -1;
            return false;
        }

        return true;
    }

    private static bool TryReadCondensedResinCount(ImageRegion region, out int count, out string raw)
    {
        count = -1;
        raw = string.Empty;
        var icon = region.Find(ElementRecognition.Get("CondensedResinCount", region));
        if (icon.IsEmpty())
        {
            return false;
        }

        using var countArea = region.DeriveCrop(icon.X + icon.Width, icon.Y, icon.Width * 5 / 3, icon.Height);
        raw = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat).Trim();
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"^\s*(?<count>[0-5])\s*$");
        if (!match.Success)
        {
            return false;
        }

        count = StringUtils.TryParseInt(match.Groups["count"].Value, -1);
        return count is >= 0 and <= 5;
    }

    private static void EmitCraftingEvent(string status, CraftingResinCounts before, CraftingResinCounts after,
        int crafts, int minResinToKeep)
    {
        string payload = JsonConvert.SerializeObject(new
        {
            task = "合成树脂",
            status,
            evidenceVerified = true,
            originalResinBefore = before.OriginalResin,
            originalResinAfter = after.OriginalResin,
            condensedResinBefore = before.CondensedResin,
            condensedResinAfter = after.CondensedResin,
            condensedResinCreated = crafts,
            originalResinSpent = crafts * 60,
            minResinToKeep
        });
        Logger.LogInformation("[AUTO_GAME_EVENT] " + payload);
    }

    private sealed record CraftingResinCounts(int OriginalResin, int CondensedResin);

    private sealed class CraftingOutcomeUnknownException : Exception
    {
        public CraftingOutcomeUnknownException(string message) : base(message)
        {
        }

        public CraftingOutcomeUnknownException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    private sealed class CraftingActionNotCommittedException : Exception
    {
        public CraftingActionNotCommittedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 前往合成台
    /// </summary>
    /// <param name="country"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task GoToCraftingBench(string country, CancellationToken ct)
    {
        Logger.LogInformation("→ {Name} 开始", Name);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                await GoToCraftingBenchOnce(country, ct);
                break;
            }
            catch (Exception e)
            {
                Logger.LogError("前往合成台执行异常：" + e.Message);
                if (i == _retryTimes - 1)
                {
                    throw;
                }

                await Delay(1000, ct);
                Logger.LogInformation("重试前往合成台");
            }
        }

        Logger.LogInformation("→ {Name} 结束", Name);
    }

    public async Task GoToCraftingBenchOnce(string country, CancellationToken ct)
    {
        var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"GameTask\Common\Element\Assets\Json\合成台_{country}.json"));
        if (task == null)
        {
            throw new Exception("地图追踪文件加载失败");
        }

        var pathingTask = new PathExecutor(ct)
        {
            PartyConfig = new PathingPartyConfig
            {
                Enabled = true,
                AutoSkipEnabled = true,
                // 合成台路线都很短；自动奔跑容易越过最后的交互范围，重试成本反而更高。
                AutoRunEnabled = false,
            },
            EndAction = region => Bv.FindFAndPress(region, text: this.craftLocalizedString)
        };
        await pathingTask.Pathing(task);

        await Delay(700, ct);
        
        // EndAction 可能已经按下“合成”，低帧率时对话界面出现会明显晚于固定 700ms。
        bool enteredCraftingTalk = await WaitForCraftingTalkUi(ct);
        if (!enteredCraftingTalk)
        {
            await TryPressCrafting(ct);
            enteredCraftingTalk = await WaitForCraftingTalkUi(ct);
        }

        if (!enteredCraftingTalk)
        {
            // 往回走一小步重新搜索“合成”提示。
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
            await Delay(250, ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            await TryPressCrafting(ct);
            enteredCraftingTalk = await WaitForCraftingTalkUi(ct);
        }

        if (!enteredCraftingTalk)
        {
            // 从后退位置向前越过原位置一小步，再做最后一次带文本约束的交互。
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(500, ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await TryPressCrafting(ct);
            enteredCraftingTalk = await WaitForCraftingTalkUi(ct);
        }

        if (!enteredCraftingTalk)
        {
            throw new Exception("未进入和合成台交互对话界面");
        }

        // 等待进入合成界面
        await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct,
            region => region.Find(ElementRecognition.Get("BtnWhiteConfirm", region)).IsExist()
        );
        await Delay(800, ct);
    }


    private bool IsInCraftingTalkUi()
    {
        using var ra = CaptureToRectArea();
        return Bv.IsInTalkUi(ra);
    }

    private Task<bool> WaitForCraftingTalkUi(CancellationToken ct)
    {
        return NewRetry.WaitForAction(IsInCraftingTalkUi, ct, 12, 250);
    }
    
    private async Task<bool> TryPressCrafting( CancellationToken ct)
    {
        using var ra1 = CaptureToRectArea();
        var res = Bv.FindFAndPress(ra1, text: this.craftLocalizedString);
        if (res)
        {
            await Delay(1000, ct);
        }
        return res;
    }
    
    private void InitConfigList()
    {
        Directory.CreateDirectory(OneDragonFlowConfigFolder);
        // 读取文件夹内所有json配置，按创建时间正序
        var configFiles = Directory.GetFiles(OneDragonFlowConfigFolder, "*.json");
        var configs = new List<OneDragonFlowConfig>();

        OneDragonFlowConfig? selected = null;
        foreach (var configFile in configFiles)
        {
            var json = File.ReadAllText(configFile);
            var config = JsonConvert.DeserializeObject<OneDragonFlowConfig>(json);
            if (config != null)
            {
                configs.Add(config);
                if (config.Name == TaskContext.Instance().Config.SelectedOneDragonFlowConfigName)
                {
                    selected = config;
                }
            }
        }

        if (selected == null)
        {
            throw new Exception(
                $"未找到当前一条龙配置：{TaskContext.Instance().Config.SelectedOneDragonFlowConfigName}，禁止使用其他配置执行合成");
        }

        ConfigList.Clear();
        foreach (var config in configs)
        {
            ConfigList.Add(config);
        }

        SelectedConfig = selected;
    }
}
