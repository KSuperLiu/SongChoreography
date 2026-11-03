using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SongChoreography;

static class Program
{
    static int passed;
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(InvalidDataException){Check(true,name);return;}catch(InvalidOperationException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static AssetCatalog Catalog(int count=3)
    {
        var c=new AssetCatalog();c.characters.Add(new CharacterDefinition{id="dancer",signature="character-v1"});
        for(int i=0;i<count;i++)c.motions.Add(new MotionDefinition{id="motion-"+i,signature="motion-v1-"+i,clipEnd=4,beatTimes=Enumerable.Range(0,9).Select(n=>n*.5).ToList(),reviewedCharacters=new List<string>{"dancer"}});
        foreach(var a in c.motions)foreach(var b in c.motions)c.transitions.Add(new TransitionDefinition{from=a.id,to=b.id,characterId="dancer"});
        return c;
    }
    static DanceProject Project()
    {
        var a=new MusicAnalysis{durationSeconds=40,bpm=120};
        for(int i=0;i<80;i++){a.beats.Add(i*.5);a.energy.Add(.5f);}
        return new DanceProject{characterId="dancer",characterSignature="character-v1",analysis=a,loopEnd=40};
    }
    static void Main()
    {
        var c=Catalog();var p=Project();p.segments=Choreographer.Generate(p,c);Choreographer.ValidateProject(p,c);
        Check(p.segments.Count==10,"forty seconds makes ten eight-count slots");
        var first=p.segments.Select(s=>s.motionId).ToArray();Check(first.SequenceEqual(Choreographer.Generate(p,c).Select(s=>s.motionId)),"seed reproduces sequence");
        Check(first.Distinct().Count()>1,"uses multiple eligible motions");
        var varied=Project();var repertoire=Catalog(8);
        varied.analysis.durationSeconds=32;varied.loopEnd=32;varied.analysis.beats=varied.analysis.beats.Take(64).ToList();varied.analysis.energy=varied.analysis.energy.Take(64).ToList();
        // Include the former three-phrase cycle as well as an eight-phrase
        // route. Energy preference must not trap generation in the old cycle.
        repertoire.transitions.Clear();
        for(int i=0;i<8;i++){repertoire.motions[i].energy=i<3?.5f:0;repertoire.transitions.Add(new TransitionDefinition{characterId="dancer",from="motion-"+i,to="motion-"+((i+1)%8)});}
        repertoire.transitions.Add(new TransitionDefinition{characterId="dancer",from="motion-2",to="motion-0"});
        varied.segments=Choreographer.Generate(varied,repertoire);Choreographer.ValidateProject(varied,repertoire);
        Check(varied.segments.Select(s=>s.motionId).Distinct().Count()==8,"eight reachable phrases beat the former three-phrase cycle");
        var small=Choreographer.Generate(Project(),Catalog(2));
        Check(small.GroupBy(s=>s.motionId).Select(g=>g.Count()).Max()<=6,"small repertoire remains playable with balanced repeats");
        varied.segments[5].locked=true;var lockedId=varied.segments[5].motionId;
        Check(Choreographer.Generate(varied,repertoire)[5].motionId==lockedId,"diversity preference preserves locked reachable phrase");
        Check(p.segments.All(s=>s.endSeconds-s.startSeconds==4),"complete slots retain eight beats");
        Check(Choreographer.Eligible(Catalog(24),"dancer").Count==24,"catalog is not capped at eight clips");
        var unreviewed=Catalog();unreviewed.motions[0].reviewedCharacters.Clear();unreviewed.transitions.RemoveAll(e=>e.from=="motion-0"||e.to=="motion-0");
        Check(!Choreographer.Generate(Project(),unreviewed).Any(s=>s.motionId=="motion-0"),"unreviewed candidate excluded");
        var selection=Choreographer.Generate(Project(),Catalog(),new[]{"motion-0","motion-1"});
        Check(selection.All(s=>s.motionId!="motion-2")&&selection.Select(s=>s.motionId).Distinct().Count()==2,"library selection limits automatic generation to checked motions");
        Reject(()=>Choreographer.Generate(Project(),unreviewed,new[]{"motion-0"}),"checking an unreviewed motion cannot bypass its review");
        Reject(()=>Choreographer.Generate(Project(),Catalog(),Array.Empty<string>()),"empty custom selection cannot silently use the whole library");
        p.segments[5].locked=true;p.segments[5].motionId="motion-2";p.segments[5].motionSignature="motion-v1-2";
        Check(Choreographer.Generate(p,c)[5].motionId=="motion-2","future locked slot remains reachable");
        var disconnected=Catalog();disconnected.transitions.Clear();var before=p.segments.ToArray();Reject(()=>Choreographer.Generate(p,disconnected),"unreviewed transitions cannot silently blend");Check(p.segments.SequenceEqual(before),"failed generation leaves original sequence untouched");
        var partial=Project();partial.grid.eightCountOffset=3;partial.segments=Choreographer.Generate(partial,c);
        Check(partial.segments[0].beatCount==5&&partial.segments[0].startBeatOffset==3,"partial opening starts at requested count");
        var motion=c.motions[0];var segment=new DanceSegment{startSeconds=0,endSeconds=4,beatCount=8};
        motion.beatTimes[2]=1.2;Check(Math.Abs(Choreographer.MotionTime(motion,segment,1)-1.2)<1e-6,"confirmed nonuniform beat markers drive animation");
        Check(Math.Abs(Choreographer.MotionTime(motion,segment,4)-4)<1e-6,"final pose does not wrap to first frame");
        var changed=Catalog();changed.motions[0].signature="new-version";p=Project();p.segments=new List<DanceSegment>{new DanceSegment{motionId="motion-0",motionSignature="motion-v1-0",startSeconds=0,endSeconds=4,beatCount=8}};
        Reject(()=>Choreographer.ValidateProject(p,changed),"changed source invalidates saved motion reference");
        p.characterSignature="outdated";Reject(()=>Choreographer.ValidateProject(p,Catalog()),"changed character invalidates project");
        p=Project();p.schemaVersion=1;Reject(()=>Choreographer.ValidateProject(p,c),"legacy project is not silently migrated");
        p=Project();p.camera.fieldOfView=float.NaN;Reject(()=>Choreographer.ValidateProject(p,c),"invalid camera state rejected");
        var invalid=Catalog();invalid.motions[0].beatTimes[3]=double.NaN;Reject(()=>Choreographer.ValidateCatalog(invalid),"nonfinite beat markers rejected");
        invalid=Catalog();invalid.motions[0].beatTimes[3]=.1;Reject(()=>Choreographer.ValidateCatalog(invalid),"backwards beat markers rejected");
        Reject(()=>Choreographer.ResolveLocal(Path.GetTempPath(),"../outside.wav"),"project traversal rejected");
        var a2=Project().analysis;a2.normalAudio="../outside.wav";Reject(()=>Choreographer.ValidateAnalysis(a2),"audio traversal rejected");
        p=Project();p.grid.manual=true;p.grid.bpm=0;Reject(()=>Choreographer.BeatTimes(p.analysis,p.grid),"invalid manual BPM rejected");
        p=Project();p.analysis.beats[3]=p.analysis.beats[2];Reject(()=>Choreographer.ValidateAnalysis(p.analysis),"duplicate detected beats rejected");
        Console.WriteLine("CORE TESTS: "+passed+" passed");
    }
}
