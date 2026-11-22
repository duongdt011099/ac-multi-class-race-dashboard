// gallery
const shots=[
 ['Drivers','images/dashboard.png','Drivers page with cars, teams and classes'],
 ['Race results','images/race-results.png','Race results with class positions and points'],
 ['Teams','images/teams-drivers.png','Teams and their drivers'],
 ['Championship','images/championship.png','Championship standings']];
const tabs=document.getElementById('tabs'),img=document.getElementById('shot'),
      nameEl=document.getElementById('shotname'),bar=document.getElementById('progress');
let k=0,auto=null;
function show(i){
  k=i;
  img.style.animation='none';img.offsetWidth;img.style.animation='';
  img.src=shots[i][1];img.alt=shots[i][2];nameEl.textContent=shots[i][0];
  [...tabs.children].forEach((t,j)=>{t.classList.toggle('on',i===j);t.setAttribute('aria-selected',i===j)});
  bar.classList.remove('run');bar.offsetWidth;if(auto)bar.classList.add('run');
}
shots.forEach((s,i)=>{
  const b=document.createElement('button');
  b.className='tab';b.textContent=s[0];b.setAttribute('role','tab');
  b.onclick=()=>{clearInterval(auto);auto=null;show(i)};
  tabs.appendChild(b);
});
shots.slice(1).forEach(s=>{const p=new Image();p.src=s[1]});
if(!matchMedia('(prefers-reduced-motion:reduce)').matches)auto=setInterval(()=>show((k+1)%shots.length),5000);
show(0);
