using System;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class AutoBattlePresentationTests
{
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("Auto-battle: " + label); }
    private static CoopCampaignPresentationValue Flag(bool value) => new CoopCampaignPresentationValue { Kind=CoopCampaignPresentationValueKind.Boolean,Number=value?1:0 };
    private static CoopCampaignPresentationValue Number(int value) => new CoopCampaignPresentationValue { Kind=CoopCampaignPresentationValueKind.Integer,Number=value };
    private static CoopCampaignPresentationValue Text(string value) => new CoopCampaignPresentationValue { Kind=CoopCampaignPresentationValueKind.Text,Text=value };
    private static CoopCampaignAutoBattlePresentation Fixture()
    {
        var root=CoopCampaignPresentationValue.Node();
        root.Properties["IsSimulation"]=Flag(true); root.Properties["ShowScoreboard"]=Flag(true); root.Properties["IsOver"]=Flag(false);
        root.Properties["IsPaused"]=Flag(false);root.Properties["IsFastForwarding"]=Flag(false);root.Properties["MissionTimeStr"]=Text("0m 3s");
        foreach(string side in new[]{"Attackers","Defenders"}) {
            var team=CoopCampaignPresentationValue.Node();var score=CoopCampaignPresentationValue.Node();
            score.Properties["NameText"]=Text(side=="Attackers"?"Загін хоста":"Bandits");score.Properties["Remaining"]=Number(18);
            score.Properties["Dead"]=Number(0);score.Properties["Wounded"]=Number(0);team.Properties["Score"]=score;
            root.Properties[side]=team;
        }
        return new CoopCampaignAutoBattlePresentation {WindowId=Guid.NewGuid().ToString("N"),Root=root};
    }
    internal static void Run()
    {
        foreach(string scenario in new[]{"field","village","siege","sally-out","ambush","relief","citadel","hideout","naval"}) {
            var state=Fixture();
            var menu=new CoopCampaignMapMenuPresentation {MenuId="autobattle:"+state.WindowId,AutoBattle=state};
            Check(menu.IsValid() && CoopCampaignMapMenuCodec.TrySerialize(1,menu,out _),scenario+" simulation menu");
            CoopCampaignMapMenuCodec.TrySerialize(1,menu,out var bytes);
            Check(CoopCampaignMapMenuCodec.TryDeserialize(bytes,out _,out var decoded) && decoded.AutoBattle.WindowId==state.WindowId &&
                decoded.AutoBattle.Root.Properties["Attackers"].Properties["Score"].Properties["NameText"].Text=="Загін хоста","native labels roundtrip "+scenario);
            var clone=state.Clone();var root=state.Root;
            root.Properties["IsPaused"].Number=1;root.Properties["IsFastForwarding"].Number=1;
            root.Properties["Attackers"].Properties["Score"].Properties["Remaining"].Number=16;
            root.Properties["Attackers"].Properties["Score"].Properties["Dead"].Number=2;
            Check(clone.Root.Properties["IsPaused"].Number==0 && clone.Root.Properties["Attackers"].Properties["Score"].Properties["Remaining"].Number==18,"deep snapshot "+scenario);
            root.Properties["IsOver"].Number=1;root.Properties["BattleResult"]=Text("Victory");root.Properties["BattleResultIndex"]=Number(1);
            Check(CoopCampaignAutoBattleCodec.TryEncode(state,out var ended) && CoopCampaignAutoBattleCodec.TryDecode(ended,out var result) &&
                result.Root.Properties["BattleResult"].Text=="Victory" && result.Root.Properties["IsOver"].Number==1,"native terminal result "+scenario);
            menu.Aftermath=new CoopCampaignAftermathPresentation {Kind=CoopCampaignAftermathKind.Prisoners,WindowId=Guid.NewGuid().ToString("N"),Root=CoopCampaignPresentationValue.Node()};
            Check(!menu.IsValid(),"simulation cannot overlap prisoner screen "+scenario);menu.Aftermath=null;
            menu.Options.Add(new CoopCampaignMapMenuOption {Text="Attack"});Check(!menu.IsValid(),"no old campaign options under simulation "+scenario);menu.Options.Clear();
            menu.AutoBattle=null;menu.MenuId="left-simulation";
            CoopCampaignMapMenuCodec.TrySerialize(2,menu,out var close);
            Check(CoopCampaignMapMenuCodec.TryDeserialize(close,out _,out var cleared) && cleared.AutoBattle==null,"leaving simulation revokes screen "+scenario);
            foreach(int length in Enumerable.Range(0,ended.Length)) Check(!CoopCampaignAutoBattleCodec.TryDecode(ended.Take(length).ToArray(),out _),"truncated tree rejected");
            Check(!CoopCampaignAutoBattleCodec.TryDecode(ended.Concat(new byte[]{0}).ToArray(),out _),"trailing tree rejected");
        }
        var rewardState=Fixture();
        var rewardHint=CoopCampaignPresentationValue.Node();
        var rewardRow=CoopCampaignPresentationValue.Node();
        rewardRow.Properties["DefinitionLabel"]=Text("Base");rewardRow.Properties["ValueLabel"]=Text("+6.4");
        rewardHint.Properties["ReplicaTooltipRows"]=new CoopCampaignPresentationValue {Kind=CoopCampaignPresentationValueKind.List,
            Items=new System.Collections.Generic.List<CoopCampaignPresentationValue>{rewardRow}};
        rewardState.Root.Properties["RewardHint"]=rewardHint;
        var rewardCopy=rewardState.Clone();
        Check(CoopCampaignAutoBattleCodec.TryEncode(rewardState,out var rewardBytes) &&
            CoopCampaignAutoBattleCodec.TryDecode(rewardBytes,out var decodedReward) &&
            decodedReward.Root.Properties["RewardHint"].Properties["ReplicaTooltipRows"].Items[0].Properties["ValueLabel"].Text=="+6.4",
            "structured reward detail survives CAS1 transport");
        rewardRow.Properties["ValueLabel"].Text="replaced";
        Check(rewardCopy.Root.Properties["RewardHint"].Properties["ReplicaTooltipRows"].Items[0].Properties["ValueLabel"].Text=="+6.4",
            "cached reward rows cannot mutate a published clone");
        var invalid=Fixture();invalid.Root.Properties["IsSimulation"].Number=0;Check(!invalid.IsValid(),"mission scoreboard cannot impersonate auto-battle");
        invalid=Fixture();invalid.Root.Properties["ShowScoreboard"].Number=0;Check(!invalid.IsValid(),"hidden scoreboard not published");
        invalid=Fixture();invalid.Root.Properties.Remove("Attackers");Check(!invalid.IsValid(),"missing native side rejected");
        invalid=Fixture();invalid.Root.Properties["Cycle"]=invalid.Root;Check(!invalid.IsValid(),"cyclic native graph rejected");
        invalid=Fixture();invalid.WindowId=Guid.Empty.ToString("N");Check(!invalid.IsValid(),"empty lifetime rejected");
        invalid=Fixture();invalid.WindowId="ABCDEFABCDEFABCDEFABCDEFABCDEFAB";Check(!invalid.IsValid(),"noncanonical lifetime rejected");
        invalid=Fixture();invalid.Root.Properties["IsSimulation"]=null;Check(!invalid.IsValid(),"null required value rejected without throwing");
        invalid=Fixture();invalid.Root.Properties["IsOver"]=null;Check(!invalid.IsValid(),"null result flag rejected without throwing");
        invalid=Fixture();invalid.Root.Properties["BadText"]=Text("\ud800");Check(!invalid.IsValid(),"malformed UTF16 rejected");
        Console.WriteLine("Auto-battle bounded presentation/transition assertions passed.");
    }
}
