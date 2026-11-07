// gallery
const shots=[
 ['Dashboard','images/dashboard.png'],
 ['Race results','images/race-results.png'],
 ['Teams & drivers','images/teams-drivers.png'],
 ['Championship','images/championship.png'],
 ['In-game leaderboard','images/leaderboard.png']
];
const tabs=document.getElementById('tabs'),img=document.getElementById('shot');
function show(i){
  img.style.animation='none';img.offsetWidth;img.style.animation='';
  img.src=shots[i][1];
  [...tabs.children].forEach((t,j)=>t.classList.toggle('on',i===j));
}
shots.forEach((s,i)=>{const b=document.createElement('button');b.className='tab';b.textContent=s[0];b.onclick=()=>{show(i);clearInterval(auto)};tabs.appendChild(b)});
show(0);
let k=0;const auto=setInterval(()=>show(k=(k+1)%shots.length),5000);
