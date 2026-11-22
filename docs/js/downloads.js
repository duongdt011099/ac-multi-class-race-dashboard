// latest version + live download count from GitHub Releases (each part stays hidden if the request fails)
fetch('https://api.github.com/repos/duongdt011099/ac-multi-class-race-dashboard/releases?per_page=100')
  .then(r=>r.ok?r.json():Promise.reject())
  .then(rs=>{
    const latest=rs.find(r=>!r.draft&&!r.prerelease);
    if(latest)document.getElementById('relver').textContent=latest.tag_name.replace(/^v?/,'v')+' is out';
    const total=rs.reduce((a,r)=>a+r.assets.reduce((b,x)=>b+x.download_count,0),0);
    if(total<100)return; // only show the counter once it means something
    const el=document.getElementById('dlcount');
    el.dataset.n=total;
    document.getElementById('dlbox').hidden=false;
    count(el);
  }).catch(()=>{});
