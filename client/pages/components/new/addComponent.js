const apiRoot = BASE_URL + '/api/components';

$(document).ready(function () {
    $('#addComponentForm').submit(function (e) {
        e.preventDefault();
        setLoading(true);

        const body = {
            componentSKU: $('#componentSKU').val(),
            description: $('#description').val(),
            baseUnit: $('#baseUnit').val() || 'pcs',
            alternativeSKU: $('#alternativeSKU').val() || null
        };

        ajaxCall("POST", apiRoot, JSON.stringify(body), addComponentSCB, addComponentECB);
    });
});

function setLoading(on) {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
}

function addComponentSCB() {
    window.location.href = '/pages/components';
}

function addComponentECB(xhr, status, err) {
    console.log('Error:', status, err);
    alert('שגיאה בשמירה, נסה שוב');
    setLoading(false);
}
