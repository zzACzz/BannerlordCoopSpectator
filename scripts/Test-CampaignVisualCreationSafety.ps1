param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Use Windows PowerShell 5.1.'}
. (Join-Path $PSScriptRoot 'Test-MapVisualReadiness.ps1') -CandidateDirectory $CandidateDirectory
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $gameBin='C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client'
    $references=@('TaleWorlds.Library.dll','TaleWorlds.ObjectSystem.dll','TaleWorlds.Core.dll') | ForEach-Object {Join-Path $gameBin $_}
    $references += 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/netstandard.dll'
    Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
public static class CampaignVisualCreationSafetyCheck
{
    static readonly BindingFlags Flags = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static void Check(bool condition, string message) { if(!condition) throw new Exception(message); }
    static void Set(object value, Type type, string property, object data)
    { type.GetProperty(property, Flags).GetSetMethod(true).Invoke(value, new[]{data}); }
    static ItemObject Item(string id)
    {
        var item=new ItemObject(id);
        Set(item,typeof(MBObjectBase),"IsReady",true);
        Set(item,typeof(MBObjectBase),"IsInitialized",true);
        return item;
    }
    static void AddWeapon(ItemObject item)
    {
        var component=new WeaponComponent(item);
        component.AddWeapon((WeaponComponentData)FormatterServices.GetUninitializedObject(typeof(WeaponComponentData)),null);
        Set(item,typeof(ItemObject),"ItemComponent",component);
    }
    public static void Run(Assembly assembly)
    {
        var factory=assembly.GetType("CoopSpectator.UI.CoopCampaignAgentVisualFactory",true);
        var construct=factory.GetMethod("ConstructOwned",Flags).MakeGenericMethod(typeof(object),typeof(object));
        object partial=null, result=new object(); int retired=0;
        Func<object> get=()=>partial;
        Action<object> retire=value=>{Check(Object.ReferenceEquals(value,partial),"Different allocation retired");retired++;partial=null;};
        var returned=construct.Invoke(null,new object[]{new Func<object>(()=>result),get,retire});
        Check(Object.ReferenceEquals(result,returned)&&retired==0,"Success changed ownership");
        var original=new InvalidOperationException("original creation failure");
        Func<object> failBefore=()=>{throw original;};
        try {construct.Invoke(null,new object[]{failBefore,get,retire});throw new Exception("Missing failure");}
        catch(TargetInvocationException ex){Check(Object.ReferenceEquals(ex.InnerException,original)&&retired==0,"Preallocation failure changed");}
        Func<object> failAfter=()=>{partial=new object();throw original;};
        try {construct.Invoke(null,new object[]{failAfter,get,retire});throw new Exception("Missing partial failure");}
        catch(TargetInvocationException ex){Check(Object.ReferenceEquals(ex.InnerException,original)&&retired==1&&partial==null,"Partial allocation leaked or original failure replaced");}
        var cleanup=new InvalidOperationException("cleanup failure");
        Action<object> brokenRetire=value=>{throw cleanup;};
        try {construct.Invoke(null,new object[]{failAfter,get,brokenRetire});throw new Exception("Missing cleanup failure");}
        catch(TargetInvocationException ex)
        {
            Check(ex.InnerException.GetType().Name=="CoopCampaignVisualCleanupException","Unsafe fallback after cleanup failure");
            var aggregate=ex.InnerException.InnerException as AggregateException;
            Check(aggregate!=null&&Object.ReferenceEquals(aggregate.InnerExceptions[0],original)&&Object.ReferenceEquals(aggregate.InnerExceptions[1],cleanup),"Lost failure provenance");
        }
        var resolver=assembly.GetType("CoopSpectator.Infrastructure.CoopCampaignVisualItemResolver",true);
        var usable=resolver.GetMethod("IsUsable",Flags);
        Func<ItemObject,int,bool> valid=(item,slot)=>(bool)usable.Invoke(null,new object[]{item,slot});
        var originalShield=Item("northern_round_shield");
        Check(!valid(originalShield,1),"r82 initialized metadata-only shield admitted");
        Set(originalShield,typeof(ItemObject),"ItemComponent",new WeaponComponent(originalShield));
        Check(!valid(originalShield,1),"Empty weapon list admitted");
        Set(originalShield,typeof(ItemObject),"ItemComponent",null);
        var mirror=Item("cs_mirror_northern_round_shield_eadc3638");AddWeapon(mirror);
        for(int slot=0;slot<=4;slot++) Check(valid(mirror,slot),"Complete weapon rejected");
        Check(!valid(mirror,5)&&!valid(mirror,10)&&!valid(mirror,11),"Wrong slot admitted");
        var armor=Item("armor");Set(armor,typeof(ItemObject),"ItemComponent",FormatterServices.GetUninitializedObject(typeof(ArmorComponent)));
        foreach(int slot in new[]{5,6,7,8,9,11}) Check(valid(armor,slot),"Armor/harness rejected");
        Set(mirror,typeof(MBObjectBase),"IsReady",false);Check(!valid(mirror,1),"Unready item admitted");Set(mirror,typeof(MBObjectBase),"IsReady",true);
        var manager=(MBObjectManager)FormatterServices.GetUninitializedObject(typeof(MBObjectManager));
        var recordsField=typeof(MBObjectManager).GetField("ObjectTypeRecords",Flags);
        var records=(IList)Activator.CreateInstance(recordsField.FieldType);
        var recordType=typeof(MBObjectManager).GetNestedType("ObjectTypeRecord`1",Flags).MakeGenericType(typeof(ItemObject));
        var record=Activator.CreateInstance(recordType,Flags,null,new object[]{(uint)1,"Item","Items",false,false},null);
        var registered=(IDictionary)recordType.GetField("_registeredObjects",Flags).GetValue(record);
        registered.Add(originalShield.StringId,originalShield);registered.Add(mirror.StringId,mirror);
        records.Add(record);recordsField.SetValue(manager,records);
        var instance=typeof(MBObjectManager).GetProperty("Instance",Flags);
        object prior=instance.GetValue(null,null);Set(null,typeof(MBObjectManager),"Instance",manager);
        try
        {
            var resolve=resolver.GetMethod("TryResolve",Flags);
            object[] args={manager,originalShield.StringId,1,null};
            Check((bool)resolve.Invoke(null,args)&&Object.ReferenceEquals(args[3],mirror),"Complete preloaded mirror did not replace metadata-only original");
            object[] imageArgs={manager,originalShield.StringId,null};
            Check((bool)resolver.GetMethod("TryResolveImage",Flags).Invoke(null,imageArgs)&&Object.ReferenceEquals(imageArgs[2],mirror),"Loot image admitted a componentless original");
            string equipmentCode="";
            for(int slot=0;slot<12;slot++) equipmentCode+="+"+slot+"-"+(slot==1?originalShield.StringId:"@null")+"-@null";
            var valueType=assembly.GetType("CoopSpectator.Infrastructure.CoopCampaignPresentationValue",true);
            var kindType=valueType.GetProperty("Kind").PropertyType;
            object root=valueType.GetMethod("Node").Invoke(null,null), text=Activator.CreateInstance(valueType);
            Set(text,valueType,"Kind",Enum.Parse(kindType,"Text"));Set(text,valueType,"Text",equipmentCode);
            ((IDictionary)valueType.GetProperty("Properties").GetValue(root,null)).Add("EquipmentCode",text);
            var vmType=assembly.GetType("CoopSpectator.UI.CoopCampaignAftermathVM",true);
            object vm=Activator.CreateInstance(vmType);
            vmType.GetMethod("Apply").Invoke(vm,new[]{root});
            string resolvedCode=(string)typeof(TaleWorlds.Library.IViewModel).GetMethod("GetPropertyValue",new[]{typeof(string)}).Invoke(vm,new object[]{"EquipmentCode"});
            Check(resolvedCode==equipmentCode.Replace(originalShield.StringId,mirror.StringId),"Actual aftermath preview retained the r84 invalid shield");
            vmType.GetMethod("OnFinalize").Invoke(vm,null);
            AddWeapon(originalShield);args[3]=null;
            Check((bool)resolve.Invoke(null,args)&&Object.ReferenceEquals(args[3],originalShield),"Valid original unnecessarily replaced");
            Set(originalShield,typeof(ItemObject),"ItemComponent",null);Set(mirror,typeof(ItemObject),"ItemComponent",null);args[3]=null;
            Check(!(bool)resolve.Invoke(null,args)&&args[3]==null,"Incomplete mirror admitted");
        }
        finally {Set(null,typeof(MBObjectManager),"Instance",prior);}
    }
}
'@
    [CampaignVisualCreationSafetyCheck]::Run($assembly)
    'PASS compiled partial-construction cleanup, exception preservation/escalation, r82/r84 shield fixtures, item-image selection and actual aftermath preview equipment resolution. Managed fixtures only; no native object or scene constructed.'
} finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
