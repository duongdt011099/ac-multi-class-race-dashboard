// live download count from GitHub Releases (hidden if the request fails)
fetch('https://api.github.com/repos/duongdt011099/ac-multi-class-race-dashboard/releases?per_page=100')
  .then(r=>r.ok?r.json():Promise.reject())
  .then(rs=>{
    const total=rs.reduce((a,r)=>a+r.assets.reduce((b,x)=>b+x.download_count,0),0);
    if(total<1)return;
    const el=document.getElementById('dlcount');
    el.dataset.n=total;
    document.getElementById('dlbox').hidden=false;
    count(el);
  }).catch(()=>{});

