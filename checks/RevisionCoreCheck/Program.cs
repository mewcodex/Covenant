using Covenant.Core;

var assertions=0;
void Check(bool passed,string reason){assertions++;if(!passed)throw new Exception(reason);}
void Reject(Action operation,string reason)
{
    try{operation();}catch(InvalidDataException){assertions++;return;}
    throw new Exception(reason);
}
Check(BondCatalog.All.Count==44&&BondCatalog.Get(Bond.Aid) is {Basic:2,Advanced:4}&&
    BondCatalog.Get(Bond.Channel) is {Basic:2,Advanced:4},"44 covenants with exact Aid and Channel thresholds");
Check(BondCatalog.Get(Bond.Breach) is {Basic:3,Advanced:6}&&
    BondCatalog.Get(Bond.Rotation) is {Basic:4,Advanced:9}&&
    BondCatalog.Get(Bond.Initiative) is {Basic:2,Advanced:4},"New covenant threshold values");
Check((int)Bond.Solitude==32&&(int)Bond.Vakuu==33&&(int)Bond.Tony==34,"Saved enum identity");
foreach(var definition in BondCatalog.All)
{
    Check(definition.Tier(definition.Basic-1)==0,"Below activation: "+definition.Name);
    Check(definition.Tier(definition.Basic)==1,"Basic activation: "+definition.Name);
    if(definition.Advanced>0)Check(definition.Tier(definition.Advanced)==2,"Advanced activation: "+definition.Name);
    else Check(definition.Tier(1000)==1,"Secondary covenant has no stage II: "+definition.Name);
    if(definition.UsesLayers)Check(definition.LayerUnit==50,"Proposal layer unit: "+definition.Name);
}
Check(BondCatalog.Get(Bond.Vakuu).Basic==5&&BondCatalog.Get(Bond.Tony).Basic==5,"User's secondary thresholds");
Check(BondCatalog.Get(Bond.Harmony) is {Basic:1,Advanced:0}&&BondCatalog.Get(Bond.Guard) is {Basic:6,Advanced:11}&&
    BondCatalog.Get(Bond.Zero) is {Basic:3,Advanced:8}&&BondCatalog.Get(Bond.Strike) is {Basic:3,Advanced:8},"Thresholds distinguish scarce and common covenants");
Check(BondCatalog.Get(Bond.Blood) is {Basic:2,Advanced:4}&&BondCatalog.Get(Bond.Ember) is {Basic:3,Advanced:9},"Blood and Ember use their updated thresholds");
Check(BondCatalog.Get(Bond.Ancient) is {Basic:1,Advanced:3,UsesLayers:false}&&BondCatalog.Get(Bond.Overload) is {Basic:2,Advanced:4},"Ancient and Overload use their revised card thresholds");
Check(BondCatalog.Get(Bond.Solitude) is {Basic:10,Advanced:25,UsesLayers:false},"Solitude has 10/25 thresholds and no layers");
Check(ScalingText.Amount(new Scale(275,50),"5 + 2*A") == "[scaled]15[/scaled][formula] (=5+[scaled]275[/scaled]/25)[/formula]"&&
    !ScalingText.Amount(new Scale(275,50),"1+floor(A/2)").Contains('⌊'),"Formula display reduces the ratio and omits truncation symbols without changing actual values");
Check(ScalingText.Amount("1",new Scale(275,50),"A/5").Contains("[scaled]275[/scaled]/250"),"An implicit layer divisor is combined with the layer unit");
var ledger=new Ledger();
ledger.Add(Bond.Poison,700);ledger.Add(Bond.Poison,500);
Check(ledger.Permanent(Bond.Poison)==999&&ledger.Get(Bond.Poison)==999,"Only permanent growth counts toward the layer cap");
var restored=Ledger.Load(ledger.Save());
Check(restored.Permanent(Bond.Poison)==999,"Battle save preserves permanent layers");
restored.ClearBattle();
Check(restored.Get(Bond.Poison)==999,"Battle cleanup preserves permanent growth");
foreach(var bond in new[]{Bond.Harmony,Bond.Ancient,Bond.Solitude,Bond.Vakuu})
{
    restored.Add(bond,999);
    Check(restored.Get(bond)==0,"No layers for "+bond);
}
Check(!Ledger.Load("{\"Schema\":3,\"Layers\":{\"32\":250}}").Layers.ContainsKey(Bond.Solitude),"obsolete Solitude layers are removed when loading");
restored.Add(Bond.Tony,1000);
Check(restored.Get(Bond.Tony)==Ledger.MaxLayers,"Tony's streak counter shares the global cap");
restored.Layers[Bond.Tony]=1200;
Check(Ledger.Load(restored.Save()).Get(Bond.Tony)==Ledger.MaxLayers,"Serialization clamps legacy or directly populated streak counters");
var overCap=Ledger.Load("{\"Schema\":3,\"Layers\":{\"3\":1400,\"34\":1200}}");
Check(overCap.Layers[Bond.Poison]==Ledger.MaxLayers&&overCap.Layers[Bond.Tony]==Ledger.MaxLayers,"Loading clamps the stored layers of both standard and secondary covenants");
restored.ClearBattle();Check(restored.Get(Bond.Tony)==0,"Tony streak is battle-only");
var migrated=Ledger.Load("{\"Schema\":3,\"Layers\":{\"3\":30},\"Temporary\":{\"3\":10},\"Units\":{\"3\":10}}");
Check(migrated.Get(Bond.Poison)==150,"Legacy temporary layers are discarded while permanent layers are migrated");
Check(Ledger.Load(migrated.Save()).Get(Bond.Poison)==150,"Migration is idempotent");
var old=Ledger.Load("{\"Schema\":2,\"Layers\":{\"3\":17},\"Units\":{\"3\":10}}");
Check(old.Permanent(Bond.Poison)==85,"0.4 schema migration");
var fraction=Ledger.Load("{\"Schema\":2,\"Layers\":{\"3\":1},\"Units\":{\"3\":30}}");
Check(fraction.Permanent(Bond.Poison)==1&&fraction.Remainders[Bond.Poison]==new LayerRemainder(2,3),"Migration preserves fractional progress");
fraction.Add(Bond.Poison,2000);Check(!fraction.Remainders.ContainsKey(Bond.Poison),"A full balance cannot retain hidden overflow");
Check(new Scale(49,50).A==0&&new Scale(50,50).A==1&&new Scale(999,50).A==19,"Proposal boundaries");
decimal Eval(string expression)=>RuleExpression.Evaluate(expression,key=>key switch{"A"=>5,"drawn_count"=>3,"before_target_PoisonPower"=>0,_=>throw new InvalidDataException(key)});
Check(Eval("2+3*4")==14&&Eval("(2+3)*4")==20,"Operator precedence");
Check(Eval("floor(A/2)*drawn_count")==6,"Fractional formulas round explicitly");
Check(Eval("before_target_PoisonPower==0&&drawn_count>0")==1,"Conjunction with zero-valued metrics");
Check(Eval("!(A<5)||drawn_count==0")==1,"Boolean operators");
Check(Eval("max(10,40-A)")==35&&Eval("min(3,drawn_count)")==3,"Numeric functions");
Reject(()=>Eval("missing_metric"),"Unknown conditions must not silently pass");
Reject(()=>Eval("A/0"),"Invalid rules must fail");
Reject(()=>Eval("floor(A/2"),"Unbalanced expressions must fail");
Console.WriteLine($"REVISION_CORE_CHECK_OK assertions={assertions}");
