// 3D tilt on hero
const tilt=document.getElementById('tilt');
if(matchMedia('(hover:hover)').matches){
  document.querySelector('.hero').addEventListener('mousemove',e=>{
    const x=e.clientX/innerWidth-.5,y=e.clientY/innerHeight-.5;
    tilt.style.transform=`rotateY(${-12+x*14}deg) rotateX(${5-y*10}deg)`;
  });
}

// card spotlight
document.querySelectorAll('.card').forEach(c=>c.addEventListener('mousemove',e=>{
  const r=c.getBoundingClientRect();
  c.style.setProperty('--mx',(e.clientX-r.left)+'px');c.style.setProperty('--my',(e.clientY-r.top)+'px');
}));

