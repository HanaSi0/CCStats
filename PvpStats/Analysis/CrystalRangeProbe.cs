using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace PvpStats.Analysis;
public sealed class CrystalRangeSample {
    public DateTime Timestamp { get; set; }
    public bool Inside { get; set; }
    public float PlayerX { get; set; }
    public float PlayerY { get; set; }
    public float PlayerZ { get; set; }
    public float CrystalX { get; set; }
    public float CrystalY { get; set; }
    public float CrystalZ { get; set; }
    public float HorizontalDistance { get; set; }
}
public sealed class CrystalRangeProbe {
    public uint Territory { get; private set; }
    public uint Entity { get; private set; }
    public List<CrystalRangeSample> Samples { get; } = new();
    public float? InsideBound=>Samples.Where(s=>s.Inside).Select(s=>(float?)s.HorizontalDistance).Max();
    public float? OutsideBound=>Samples.Where(s=>!s.Inside).Select(s=>(float?)s.HorizontalDistance).Min();
    public bool Consistent=>InsideBound.HasValue&&OutsideBound.HasValue&&InsideBound.Value<OutsideBound.Value;
    public float? ReferenceRadius=>Consistent?(InsideBound!.Value+OutsideBound!.Value)/2:null;
    public void Scope(uint territory,uint entity) {
        if(Territory==territory&&Entity==entity)return;
        Territory=territory;Entity=entity;Samples.Clear();
    }
    public void Clear()=>Samples.Clear();
    public bool Mark(DateTime time,Vector3 player,Vector3 crystal,bool inside) {
        if(Entity==0||!ReplayScene3D.Finite(player)||!ReplayScene3D.Finite(crystal)||Samples.Count>=256)return false;
        var distance=Vector2.Distance(new(player.X,player.Z),new(crystal.X,crystal.Z));
        if(distance>100)return false;
        Samples.Add(new() { Timestamp=time,Inside=inside,PlayerX=player.X,PlayerY=player.Y,PlayerZ=player.Z,CrystalX=crystal.X,CrystalY=crystal.Y,CrystalZ=crystal.Z,HorizontalDistance=distance });return true;
    }
}
