using System.Text.Json;

namespace QbPotDoubanAi;

internal static class DoubanOfficialFormScripts
{
    private sealed record SubmitPayload(
        string SubjectId,
        string Status,
        int? Rating,
        string Comment);

    /// <summary>
    /// Uses the official visible form and requestSubmit(). It never calls fetch, never reads cookies,
    /// and never writes the hidden rating value directly.
    /// </summary>
    public static string BuildSubmitScript(string subjectId, ResolvedReviewTarget target)
    {
        var payload = JsonSerializer.Serialize(new SubmitPayload(
            subjectId,
            target.Status,
            target.Rating,
            target.Comment));

        return $$"""
(() => {
  const payload = {{payload}};
  const visible = node => {
    if (!node) return false;
    const style = getComputedStyle(node);
    return style.display !== 'none' && style.visibility !== 'hidden' && !node.disabled;
  };
  const clean = value => String(value ?? '').replace(/\r\n/g, '\n').trim();
  const asRating = value => {
    const number = Number(value || 0);
    if (!Number.isFinite(number) || number <= 0) return null;
    const normalized = number >= 10 && number % 10 === 0 ? number / 10 : number;
    return normalized >= 1 && normalized <= 5 ? normalized : null;
  };
  const containerOf = form => {
    const container = form.closest('.interest_sect_level,#interest_sect_level,#interest_sect,[class*="interest"]');
    return container && !container.closest('#interest_sectl,.rating_wrap') ? container : null;
  };
  const forms = [...document.querySelectorAll('form')].filter(form => {
    if (!visible(form) || !containerOf(form)) return false;
    const action = form.getAttribute('action') || '';
    if (/accounts\/login|passport\/login/i.test(action)) return false;
    return !!form.querySelector('input[name="interest"],input[name="rating"],textarea[name="comment"]');
  });
  if (forms.length !== 1) {
    return { submitted:false, error:'官方编辑表单数量异常', formCount:forms.length };
  }

  const form = forms[0];
  const action = form.getAttribute('action') || '';
  let actionUrl;
  try {
    actionUrl = new URL(action || location.href, location.href);
  } catch {
    return { submitted:false, error:'官方表单地址无效', action };
  }
  if (actionUrl.origin !== location.origin) {
    return { submitted:false, error:'拒绝跨域官方表单', action:actionUrl.href };
  }
  if (!actionUrl.pathname.includes(`/subject/${payload.SubjectId}/`) &&
      !actionUrl.pathname.includes(`/subject/${payload.SubjectId}`)) {
    return { submitted:false, error:'官方表单影片ID不匹配', actionPath:actionUrl.pathname };
  }

  const interests = [...form.querySelectorAll('input[name="interest"]')];
  const findInterest = status => [...form.querySelectorAll('input[name="interest"]')].find(node =>
    String(node.value || '').toLowerCase() === String(status || '').toLowerCase());
  const activateInterest = status => {
    const node = findInterest(status);
    if (!node) return { ok:false, error:'官方表单不提供请求状态', status };
    if (node.type === 'radio' || node.type === 'checkbox') {
      node.click();
      node.dispatchEvent(new Event('input', { bubbles:true }));
      node.dispatchEvent(new Event('change', { bubbles:true }));
    }
    const current = findInterest(status);
    if ((current?.type === 'radio' || current?.type === 'checkbox') && !current.checked)
      return { ok:false, error:'官方状态控件未更新', status };
    return { ok:true, node:current || node };
  };
  const targetInterest = findInterest(payload.Status);
  if (!targetInterest) {
    return {
      submitted:false,
      error:'官方表单不提供请求状态',
      availableStatuses:interests.map(node => String(node.value || '')).filter(Boolean)
    };
  }
  const initialStatusActivation = activateInterest(payload.Status);
  if (!initialStatusActivation.ok) {
    return { submitted:false, error:initialStatusActivation.error, status:initialStatusActivation.status };
  }

  const ratingHidden = form.querySelector('input[name="rating"]');
  const readRating = () => asRating(form.querySelector('input[name="rating"]')?.value);
  const ratingCandidates = [...form.querySelectorAll(
    'img[id^="star"],button[id^="star"],a[id^="star"],[data-rating],[data-value],[role="radio"],input[type="radio"][name="rating"]'
  )].filter(visible);
  const candidateRating = node => {
    const idHit = String(node.id || '').match(/star[_-]?([1-5])$/i);
    return asRating(
      node.getAttribute('data-rating') ||
      node.getAttribute('data-value') ||
      node.value ||
      node.getAttribute('aria-label')?.match(/[1-5]/)?.[0] ||
      idHit?.[1]
    );
  };

  let ratingClearMethod = '';
  if (payload.Rating == null) {
    if (readRating() != null) {
      const clearControls = [...new Set([
        ...ratingCandidates,
        ...form.querySelectorAll('a,button,input,[role="button"],[data-rating],[data-value],[role="radio"]')
      ])]
        .filter(visible)
        .filter(node => {
          const text = clean(node.value || node.textContent || node.title || node.getAttribute('aria-label'));
          const raw = String(node.getAttribute('data-rating') || node.getAttribute('data-value') || node.value || '');
          const explicitZeroId = /star[_-]?0$/i.test(String(node.id || ''));
          const zero = /^(0|none|null)$/i.test(raw);
          return explicitZeroId || zero || /取消评分|清除评分|删除评分|不评分|无评分/.test(text);
        });
      if (clearControls.length === 1) {
        clearControls[0].click();
        clearControls[0].dispatchEvent(new Event('input', { bubbles:true }));
        clearControls[0].dispatchEvent(new Event('change', { bubbles:true }));
        ratingClearMethod = 'explicit-control';
        if (readRating() != null) {
          return { submitted:false, error:'官方评分清除控件未生效', ratingAfterClear:readRating(), ratingClearMethod };
        }
      } else if (clearControls.length === 0 &&
                 String(payload.Status || '').toLowerCase() === 'wish') {
        // 豆瓣当前表单在切换“想看”时不会立即把隐藏 rating 字段清空；
        // 评分由服务器在正式提交“想看”后清除。因此这里允许提交，但最终成功仍必须由
        // 导航结算后的官方表单回读确认 status=wish 且 rating=null。
        ratingClearMethod = 'wish-server-submit';
      } else {
        // do/collect 清除评分必须由协调器先完成一次独立的“想看”官方事务，
        // 等服务器回读确认评分为空后，再提交最终状态。禁止在同一未提交表单里伪造清分。
        return {
          submitted:false,
          error:'清除评分需要先完成想看状态的官方结算',
          clearCandidateCount:clearControls.length,
          requiresWishClearTransaction:true,
          clearCandidates:clearControls.map(node => ({
            tag:String(node.tagName || ''),
            id:String(node.id || ''),
            value:String(node.value || ''),
            dataRating:String(node.getAttribute('data-rating') || ''),
            dataValue:String(node.getAttribute('data-value') || ''),
            text:clean(node.textContent || node.title || node.getAttribute('aria-label'))
          })),
          ratingCandidates:ratingCandidates.map(node => ({
            tag:String(node.tagName || ''),
            id:String(node.id || ''),
            value:String(node.value || ''),
            dataRating:String(node.getAttribute('data-rating') || ''),
            dataValue:String(node.getAttribute('data-value') || '')
          }))
        };
      }
    }
  } else {
    const candidates = ratingCandidates.filter(node => candidateRating(node) === payload.Rating);
    if (candidates.length !== 1) {
      return { submitted:false, error:'官方评分控件不唯一或缺失', rating:payload.Rating, candidateCount:candidates.length };
    }
    candidates[0].click();
    candidates[0].dispatchEvent(new Event('input', { bubbles:true }));
    candidates[0].dispatchEvent(new Event('change', { bubbles:true }));
    if (readRating() !== payload.Rating) {
      return { submitted:false, error:'官方评分控件未更新', requested:payload.Rating, actual:readRating() };
    }
  }

  const commentNode = form.querySelector('textarea[name="comment"],textarea');
  if (!commentNode) {
    return { submitted:false, error:'官方短评控件缺失' };
  }
  commentNode.focus();
  commentNode.value = payload.Comment;
  commentNode.dispatchEvent(new Event('input', { bubbles:true }));
  commentNode.dispatchEvent(new Event('change', { bubbles:true }));
  if (clean(commentNode.value) !== clean(payload.Comment)) {
    return { submitted:false, error:'官方短评控件未更新' };
  }

  const currentInterests = [...form.querySelectorAll('input[name="interest"]')];
  const selectedStatus = currentInterests.find(node => node.checked)?.value ||
    (currentInterests.length === 1 && currentInterests[0].type === 'hidden' ? currentInterests[0].value : '');
  const prepared = {
    status:String(selectedStatus || '').toLowerCase(),
    rating:readRating(),
    comment:clean(commentNode.value),
    ratingClearMethod
  };
  const wishServerClearPending =
    ratingClearMethod === 'wish-server-submit' &&
    String(payload.Status || '').toLowerCase() === 'wish' &&
    payload.Rating == null;
  const ratingPrepared = prepared.rating === payload.Rating || wishServerClearPending;
  if (prepared.status !== String(payload.Status).toLowerCase() ||
      !ratingPrepared ||
      prepared.comment !== clean(payload.Comment)) {
    return {
      submitted:false,
      error:'提交前官方表单复核失败',
      prepared,
      target:payload,
      wishServerClearPending
    };
  }

  const submitButton = [...form.querySelectorAll('button[type="submit"],input[type="submit"]')].find(visible);
  if (!submitButton) {
    return { submitted:false, error:'官方提交按钮缺失' };
  }

  let submitEventObserved = false;
  let submitDefaultPrevented = false;
  form.addEventListener('submit', event => {
    submitEventObserved = true;
    submitDefaultPrevented = event.defaultPrevented;
  }, { once:true, capture:true });

  try {
    form.requestSubmit(submitButton);
  } catch (error) {
    return { submitted:false, error:`requestSubmit 失败：${error?.message || error}` };
  }

  return {
    submitted:submitEventObserved,
    submitEventObserved,
    submitDefaultPrevented,
    initialUrl:location.href,
    formAction:actionUrl.href,
    prepared
  };
})()
""";
    }
}
