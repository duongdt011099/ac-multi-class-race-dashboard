// speed streaks
(function(){
  const box=document.getElementById('streaks'),cols=['#ff2e4d','#2e8bff','#1fd18b','#ffc83d'];
  for(let i=0;i<22;i++){
    const s=document.createElement('span'),c=cols[i%4];
    s.style.cssText=`top:${Math.random()*100}%;background:linear-gradient(90deg,transparent,${c});width:${120+Math.random()*260}px;animation-duration:${1.6+Math.random()*3}s;animation-delay:${-Math.random()*5}s`;
    box.appendChild(s);
  }
  const cf=document.getElementById('confetti'),cc=['#ffc83d','#ff2e4d','#2e8bff','#1fd18b','#fff'];
  for(let i=0;i<40;i++){
    const e=document.createElement('i');
    e.style.cssText=`left:${Math.random()*100}%;background:${cc[i%5]};animation-duration:${4+Math.random()*5}s;animation-delay:${-Math.random()*9}s`;
    cf.appendChild(e);
  }
})();

