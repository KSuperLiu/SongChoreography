using SongChoreography;
using System.Text.Json;

static class Tests
{
    static int passed;
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
    static void Throws(Action action,string label){bool threw=false;try{action();}catch{threw=true;}Check(threw,label);}
    static DanceProject Project()=>new DanceProject{analysis=new MusicAnalysis{durationSeconds=40,bpm=120},grid=new GridSettings{manual=true,bpm=120},loopEnd=40};
    static MotionCatalog Catalog()
    {
        var c=new MotionCatalog();for(int i=0;i<8;i++)c.motions.Add(new MotionDefinition{id="m"+i,name="Test "+i,resource="Motions/Test"+i,energy=.1f*i,safeNext=Enumerable.Range(0,8).Select(n=>"m"+n).ToList()});return c;
    }
    static void Main()
    {
        var p=Project();var c=Catalog();p.segments=Choreographer.Generate(p,c);
        Check(p.segments.Count==10&&p.segments[9].endSeconds==40,"full excerpt coverage");
        Check(p.segments.Select(s=>s.motionId).SequenceEqual(Choreographer.Generate(p,c).Select(s=>s.motionId)),"deterministic seeded generation");
        string original=p.segments[2].motionId;p.segments[2].locked=true;p.seed++;
        p.segments=Choreographer.Generate(p,c);Check(p.segments[2].motionId==original&&p.segments[2].locked,"locked segment survives regeneration");
        double end=p.segments[0].endSeconds;Choreographer.Replace(p,c,0,"m7");Check(p.segments[0].endSeconds==end,"replacement preserves timing");
        Throws(()=>Choreographer.Replace(p,c,2,"m0"),"locked replacement rejected");
        p.grid.bpm=125;Throws(()=>Choreographer.Generate(p,c),"grid shift cannot silently move locks");p.grid.bpm=120;
        foreach(var m in c.motions)m.safeNext.Clear();Throws(()=>Choreographer.Generate(p,c),"unsafe connections rejected");
        c=Catalog();p=Project();p.mode="new-jazz";Throws(()=>Choreographer.Generate(p,c),"diagnostic motions never labeled New Jazz");
        foreach(var m in c.motions){m.style="new-jazz";m.approved=true;m.source="original";m.license="CC0";m.reviewer="reviewer";m.reviewNotes="Reviewed foot contact and connections";}
        p.segments=Choreographer.Generate(p,c);Choreographer.ValidateProject(p,c);Check(p.segments.Count==10,"reviewed style accepted");
        var options=new JsonSerializerOptions{IncludeFields=true};var restored=JsonSerializer.Deserialize<DanceProject>(JsonSerializer.Serialize(p,options),options);
        Choreographer.ValidateProject(restored,c);Check(restored.segments[3].motionId==p.segments[3].motionId,"project serialization roundtrip");
        Throws(()=>Choreographer.ResolveLocal(Path.GetTempPath(),"../escape.wav"),"path traversal rejected");
        Throws(()=>Choreographer.ResolveLocal(Path.GetTempPath(),Path.GetFullPath("absolute.wav")),"absolute media path rejected");
        p.analysis.beats=new List<double>{2,1};p.analysis.energy=new List<float>{.4f,.3f};Throws(()=>Choreographer.ValidateAnalysis(p.analysis),"unsorted analysis rejected");
        p=Project();p.analysis.beats=new List<double>{0,.5,1};p.analysis.energy=new List<float>{.1f,.9f,.2f};Check(Choreographer.EnergyAt(p.analysis,.5)==.9f,"energy maps to song time");
        p=Project();p.grid.eightCountOffset=8;Throws(()=>Choreographer.BeatTimes(p.analysis,p.grid),"invalid count offset rejected");
        // Lookahead must find a path to a lock, rather than picking a dead-end greedily.
        p=Project();c=Catalog();foreach(var m in c.motions)m.safeNext=new List<string>{"m1"};c.motions[1].safeNext.Add("m2");c.motions[2].safeNext=new List<string>{"m1"};
        p.segments=new List<DanceSegment>{new DanceSegment{startSeconds=8,endSeconds=12,beatCount=8,motionId="m2",locked=true}};
        var planned=Choreographer.Generate(p,c);Check(planned[2].motionId=="m2"&&planned[1].motionId=="m1","planner respects future lock constraints");
        p=Project();c=Catalog();p.camera=null;p.character=null;p.view=2;Choreographer.ValidateProject(p,c);
        Check(p.camera.fieldOfView==40&&p.character=="female"&&p.view==2,"legacy project gets camera and character defaults");
        p.camera.yaw=132;p.camera.panX=.24f;p.camera.pitch=22;p.camera.distanceScale=.72f;p.camera.dragPans=true;p.view=6;p.character="male";
        restored=JsonSerializer.Deserialize<DanceProject>(JsonSerializer.Serialize(p,options),options);Choreographer.ValidateProject(restored,c);
        Check(restored.camera.yaw==132&&restored.camera.panX==.24f&&restored.camera.dragPans&&restored.view==6&&restored.character=="male","camera framing and character survive save/load");
        p.camera.fieldOfView=0;Throws(()=>Choreographer.ValidateProject(p,c),"invalid camera lens rejected");
        p=Project();c=Catalog();p.mode=DanceStyles.Charleston;
        foreach(var m in c.motions)m.style=DanceStyles.Charleston;
        Throws(()=>Choreographer.Generate(p,c),"unreviewed Charleston cannot become a public dance");
        foreach(var m in c.motions){m.approved=true;m.source="CMU test fixture";m.license="test usage terms";m.reviewer="fixture";m.reviewNotes="fixture contact and connection review";}
        p.segments=Choreographer.Generate(p,c);Choreographer.ValidateProject(p,c);
        Check(p.mode==DanceStyles.Charleston&&DanceStyles.Name(p.mode)=="Charleston","reviewed alternate style retains its actual identity");
        c.motions[0].groundToFloor=true;c.motions[0].supportHeights=new List<float>{.01f,float.NaN};
        Throws(()=>Choreographer.Generate(p,c),"non-finite grounding cannot enter an approved choreography");
        Throws(()=>Choreographer.Eligible(c,"charleston-review"),"development review clips cannot be a public project style");
        string runtimeFixture=Path.GetFullPath(Path.Combine("TestResults","runtime-contract-"+Guid.NewGuid().ToString("N")));
        string embedded=AnalyzerRuntime.BundledPath(runtimeFixture);Directory.CreateDirectory(Path.GetDirectoryName(embedded));File.WriteAllText(embedded,"resolver fixture; never executed");
        try
        {
            Check(AnalyzerRuntime.Resolve(runtimeFixture,"",null)==embedded&&AnalyzerRuntime.Resolve(runtimeFixture,"python",null)==embedded,"bundled engine replaces legacy system-Python default");
            Check(AnalyzerRuntime.Resolve(runtimeFixture,"custom.exe",null)=="custom.exe"&&AnalyzerRuntime.Resolve(runtimeFixture,"custom.exe","override.exe")=="override.exe","explicit analyzer preferences and environment override persist");
        }
        finally{File.Delete(embedded);Directory.Delete(Path.GetDirectoryName(embedded));Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(embedded)));Directory.Delete(runtimeFixture);}
        p=Project();c=Catalog();p.grid.eightCountOffset=3;p.segments=Choreographer.Generate(p,c);Choreographer.ValidateProject(p,c);
        Check(p.segments[0].startBeatOffset==3&&p.segments[0].beatCount==5&&p.segments[0].endSeconds==2.5&&p.segments[1].startSeconds==2.5&&p.segments[1].beatCount==8,"count four creates a five-beat leading phrase");
        Check(Choreographer.MotionCycles(p.segments[0],8,0)==3.0/8&&Choreographer.MotionCycles(p.segments[0],8,2.5)==1,"leading phrase samples count four and ends at the reviewed cycle boundary");
        double phraseEnd=p.segments[0].endSeconds;Choreographer.Replace(p,c,0,"m7");Check(p.segments[0].startBeatOffset==3&&p.segments[0].endSeconds==phraseEnd,"replacement retains leading phrase phase and duration");
        restored=JsonSerializer.Deserialize<DanceProject>(JsonSerializer.Serialize(p,options),options);Check(restored.segments[0].startBeatOffset==3&&restored.segments[0].beatCount==5,"partial phrase survives project serialization");
        p.segments[0].locked=true;p.grid.eightCountOffset=0;Throws(()=>Choreographer.Generate(p,c),"phrase correction cannot silently change a locked leading segment");
        p.grid.eightCountOffset=3;p.segments[1].startBeatOffset=1;Throws(()=>Choreographer.ValidateProject(p,c),"incoming transitions cannot begin at an unreviewed middle phase");p.segments[1].startBeatOffset=0;
        p.segments[0].startBeatOffset=-1;Throws(()=>Choreographer.ValidateProject(p,c),"negative animation phase rejected");
        p=Project();p.grid.manual=false;p.analysis.beats=Enumerable.Range(0,80).Select(i=>i*.5+(i%8==1?.04:0)).ToList();p.analysis.energy=Enumerable.Repeat(.5f,80).ToList();
        p.segments=Choreographer.Generate(p,c);Check(p.segments.Take(9).All(s=>s.beatCount==8),"uneven detected intervals cannot truncate complete eight-count phrases");
        Check(p.segments.Last().endSeconds==40,"phrase-aligned generation still covers the complete excerpt");
        Console.WriteLine($"{passed} core checks passed.");
    }
}
