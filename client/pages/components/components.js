let allComponents = [];
const apiRoot = BASE_URL + '/api/components';

$(document).ready(function () {
    getComponents();

    $('#searchInput').on('input', function () {
        const query = $(this).val().toLowerCase();
        const filtered = allComponents.filter(p => (p.description || '').toLowerCase().includes(query));
        renderComponents(filtered);
    });

    function getComponents() {
        ajaxCall("GET", apiRoot, "", getComponentsSCB, getComponentsECB);
    }

    function getComponentsSCB(data) {
        allComponents = data;
        renderComponents(data);
    }

    function getComponentsECB(xhr, status, error) {
        console.log('Error loading JSON:', status, error);
        alert('בעיה בטעינה, נסה שוב מאוחר יותר');
    }

    function renderComponents(data) {
        const componentsContainer = $('#componentsContainer');
        componentsContainer.empty();

        const rows = data.length === 0
            ? `<tr><td colspan="5" class="table-empty">אין רכיבים</td></tr>`
            : data.map(p => `
                <tr>
                    <td><img src="../../images/header/products.png" class="row-icon" alt=""/> ${p.componentSKU}</td>
                    <td>${p.description}</td>
                    <td>${p.baseUnit}</td>
                    <td>${p.alternativeSKU || '-'}</td>
                    <td>
                        <img src="../../images/edit.png" class="icon-edit" data-sku="${p.componentSKU}" alt="עריכה" style="cursor:pointer;" />
                        <img src="../../images/trash.png" class="icon-delete" data-sku="${p.componentSKU}" alt="מחק" />
                    </td>
                </tr>
            `).join('');

        componentsContainer.html(`
            <table class="data-table">
                <thead>
                    <tr>
                        <th>מק"ט</th>
                        <th>תיאור</th>
                        <th>יחידת בסיס</th>
                        <th>מק"ט חלופי</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `);

        $('.icon-delete').click(function () {
            const sku = $(this).data('sku');
            if (!confirm('האם אתה בטוח שברצונך למחוק את הרכיב?')) return;
            ajaxCall("DELETE", apiRoot + '/' + sku, "", deleteComponentSCB, deleteComponentECB);
        });

        $('.icon-edit').click(function () {
            const sku = String($(this).data('sku'));
            const comp = allComponents.find(c => String(c.componentSKU) === sku);
            if (!comp) return;
            $('#editOriginalSKU').val(comp.componentSKU);
            $('#editSKU').val(comp.componentSKU);
            $('#editDescription').val(comp.description || '');
            $('#editBaseUnit').val(comp.baseUnit || '');
            $('#editAlternativeSKU').val(comp.alternativeSKU || '');
            $('#editComponentModal').addClass('active');
        });
    }

    function deleteComponentSCB() {
        getComponents();
    }

    function deleteComponentECB(xhr, status, error) {
        console.log('Error deleting:', status, error);
        alert('שגיאה במחיקה, נסה שוב');
    }

    $('#closeComponentModal, #editComponentModal').click(function (e) {
        if (e.target === this) $('#editComponentModal').removeClass('active');
    });

    $('#editComponentForm').submit(function (e) {
        e.preventDefault();
        const originalSku = $('#editOriginalSKU').val();
        const newSku = $('#editSKU').val().trim();
        const body = {
            componentSKU: newSku,
            description: $('#editDescription').val(),
            baseUnit: $('#editBaseUnit').val(),
            alternativeSKU: $('#editAlternativeSKU').val() || null
        };
        ajaxCall("PUT", apiRoot + '/' + encodeURIComponent(originalSku), body, function () {
            $('#editComponentModal').removeClass('active');
            getComponents();
        }, function () { alert('שגיאה בשמירה, נסה שוב'); });
    });
});
