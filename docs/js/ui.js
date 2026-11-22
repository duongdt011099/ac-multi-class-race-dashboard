// nav: solid background after scrolling, highlight the section in view
const nav=document.getElementById('nav');
const onScroll=()=>nav.classList.toggle('solid',scrollY>20);
addEventListener('scroll',onScroll,{passive:true});onScroll();
const links=[...nav.querySelectorAll('ul a')];
const spy=new IntersectionObserver(es=>es.forEach(e=>{
  if(e.isIntersecting)links.forEach(a=>a.classList.toggle('on',a.getAttribute('href')==='#'+e.target.id));
}),{rootMargin:'-45% 0px -50% 0px'});
links.forEach(a=>{const s=document.querySelector(a.getAttribute('href'));if(s)spy.observe(s)});

// 3D tilt on hero window
const tilt=document.getElementById('tilt');
if(matchMedia('(hover:hover) and (min-width:1001px)').matches&&!matchMedia('(prefers-reduced-motion:reduce)').matches){
  document.querySelector('.hero').addEventListener('mousemove',e=>{
    const x=e.clientX/innerWidth-.5,y=e.clientY/innerHeight-.5;
    tilt.style.transform=`rotateY(${-10+x*12}deg) rotateX(${4-y*8}deg)`;
  });
}

// card spotlight
document.querySelectorAll('.card').forEach(c=>c.addEventListener('mousemove',e=>{
  const r=c.getBoundingClientRect();
  c.style.setProperty('--mx',(e.clientX-r.left)+'px');c.style.setProperty('--my',(e.clientY-r.top)+'px');
}));
