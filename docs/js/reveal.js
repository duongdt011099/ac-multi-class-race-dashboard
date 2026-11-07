// scroll reveal
const io=new IntersectionObserver(es=>es.forEach(e=>{
  if(!e.isIntersecting)return;
  e.target.classList.add('in');
  e.target.querySelectorAll?.('[data-n]').forEach(count);
  if(e.target.dataset.n)count(e.target);
  io.unobserve(e.target);
}),{threshold:.2});
document.querySelectorAll('.rv').forEach(el=>io.observe(el));
function count(el){
  if(el.done)return;el.done=1;
  const t=+el.dataset.n,s=el.dataset.s||'',t0=performance.now();
  (function f(n){const p=Math.min((n-t0)/1400,1);el.textContent=Math.round(t*(1-Math.pow(1-p,3)))+s;if(p<1)requestAnimationFrame(f)})(t0);
}

