// speed streaks + trophy confetti
(function(){
  if(matchMedia('(prefers-reduced-motion:reduce)').matches)return;
  const box=document.getElementById('streaks'),cols=['#e10600','#ff2e24','#2e8bff','#1fd18b','#ffffff'];
  for(let i=0;i<18;i++){
    const s=document.createElement('span'),c=cols[i%cols.length];
    s.style.cssText=`top:${Math.random()*100}%;background:linear-gradient(90deg,transparent,${c});width:${140+Math.random()*280}px;animation-duration:${1.4+Math.random()*2.6}s;animation-delay:${-Math.random()*5}s`;
    box.appendChild(s);
  }
  const cf=document.getElementById('confetti'),cc=['#ffc83d','#e10600','#2e8bff','#1fd18b','#fff'];
  for(let i=0;i<36;i++){
    const e=document.createElement('i');
    e.style.cssText=`left:${Math.random()*100}%;background:${cc[i%5]};animation-duration:${5+Math.random()*5}s;animation-delay:${-Math.random()*10}s`;
    cf.appendChild(e);
  }
})();
