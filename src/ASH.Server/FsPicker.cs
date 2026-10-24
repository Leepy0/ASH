namespace ASH.Server;

/// <summary>
/// The server-side folder/file browser of the settings page (backed by GET /admin/fs, Routes.AdminFs.cs): a browser cannot open the server's Explorer, so the
/// "찾아보기…" buttons next to the path settings (SettingDef.Pick) open this dialog. Choosing fills the input and fires "input" so the change counter follows.
/// </summary>
internal static class FsPicker
{
    public const string Html = """
<style>
#fsd{border:1px solid var(--line2);border-radius:12px;padding:0;width:min(640px,94vw);max-height:80vh;color:var(--ink)}
#fsd::backdrop{background:rgba(0,0,0,.35)}
#fsd .fh{padding:14px 16px 8px;display:flex;flex-direction:column;gap:8px}#fsd h2{margin:0;font-size:16px}
#fsd .fp{font:13px/1.4 ui-monospace,Consolas,monospace;word-break:break-all;color:var(--sub2);min-height:18px}
#fsd .fl{list-style:none;margin:0;padding:0 8px;overflow:auto;height:46vh;border-top:1px solid var(--line);border-bottom:1px solid var(--line)}
#fsd .fl button{all:unset;box-sizing:border-box;display:block;width:100%;min-height:34px;line-height:34px;padding:0 10px;border-radius:8px;cursor:pointer;font-size:14px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
#fsd .fl button:hover{background:#f0f0f5}#fsd .fl button:focus-visible{outline:3px solid var(--blue)}
#fsd .fe{padding:6px 16px;min-height:20px;font-size:13px}
#fsd .fa{display:flex;gap:8px;padding:10px 16px;align-items:center}#fsd .fa .sp{flex:1}
</style>
<dialog id="fsd" aria-labelledby="fst"><div class="fh"><h2 id="fst">찾아보기</h2><div class="fp" id="fsp"></div>
<div style="display:flex;gap:8px"><button type="button" class="btn" id="fsup">위로</button><button type="button" class="btn" id="fsdr">드라이브</button></div></div>
<ul class="fl" id="fsl"></ul><div class="fe msg bad" id="fse" role="alert" hidden></div>
<div class="fa"><span class="sp t4" id="fsh"></span><button type="button" class="btn" id="fsx">닫기</button><button type="button" class="btn pri" id="fsok">이 폴더 선택</button></div></dialog>
""";

    public const string Script = """
<script>
(function(){
  var dlg=document.getElementById('fsd'); if(!dlg||!dlg.showModal) return;
  var L=document.getElementById('fsl'), P=document.getElementById('fsp'), E=document.getElementById('fse'), up=document.getElementById('fsup'), ok=document.getElementById('fsok');
  var target=null, mode='dir', cur='', parent=null;
  function item(text,fn){ var li=document.createElement('li'), b=document.createElement('button'); b.type='button'; b.textContent=text; b.addEventListener('click',fn); li.appendChild(b); L.appendChild(li); }
  function pick(v){ target.value=v; target.dispatchEvent(new Event('input',{bubbles:true})); dlg.close(); target.focus(); }
  function load(path){
    fetch('/admin/fs?path='+encodeURIComponent(path)+(mode==='file'?'&files=1':''),{credentials:'same-origin',cache:'no-store'}).then(function(r){
      if(!r.ok) return r.text().then(function(t){ throw new Error(r.status===401||r.status===403?'관리자 로그인이 필요합니다. 페이지를 새로 고치세요.':(t||'열 수 없습니다.')); });
      return r.json();
    }).then(function(d){
      cur=d.path; parent=d.parent; L.textContent=''; P.textContent=cur||'(드라이브)';
      E.hidden=!d.error; E.textContent=d.error||'';
      up.disabled=parent===null; ok.hidden=mode==='file'; ok.disabled=cur==='';
      d.dirs.forEach(function(e){ item('📁 '+e.name,function(){ load(e.path); }); });
      d.files.forEach(function(e){ item('📄 '+e.name,function(){ pick(e.path); }); });
      if(!d.dirs.length&&!d.files.length&&!d.error) item('(비어 있음)',function(){});
      if(d.truncated) item('… 목록이 길어 앞부분만 보입니다',function(){});
    }).catch(function(e){ E.hidden=false; E.textContent=e.message; if(cur!=='') load(''); });
  }
  document.querySelectorAll('.pick').forEach(function(b){ b.addEventListener('click',function(){
    target=document.getElementById(b.getAttribute('data-for')); mode=b.getAttribute('data-pick');
    document.getElementById('fst').textContent=b.getAttribute('data-title')+' 찾아보기';
    document.getElementById('fsh').textContent=mode==='file'?'파일을 누르면 선택됩니다.':'폴더를 연 뒤 "이 폴더 선택"을 누릅니다.';
    var v=target.value.trim(), abs=/^[A-Za-z]:/.test(v)||(v.charCodeAt(0)===92&&v.charCodeAt(1)===92);
    cur=''; dlg.showModal(); load(abs?v:'');
  }); });
  up.addEventListener('click',function(){ if(parent!==null) load(parent); });
  document.getElementById('fsdr').addEventListener('click',function(){ load(''); });
  ok.addEventListener('click',function(){ if(cur) pick(cur); });
  document.getElementById('fsx').addEventListener('click',function(){ dlg.close(); });
})();
</script>
""";
}
